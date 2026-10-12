using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Projects staff-planned windows into zone capacity buckets. The result is planning
/// data only; it is neither live availability nor a dispatch authorization decision.
/// </summary>
public sealed class PostgresCourierCapacityProjectionService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresCourierOperationsReadAuditService readAudit)
{
    private static readonly int[] SupportedBucketMinutes = [15, 30, 60];

    public async Task<CourierCapacityProjection> ProjectAsync(
        string zoneId,
        DateTimeOffset from,
        DateTimeOffset to,
        int bucketMinutes,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        zoneId = zoneId?.Trim() ?? string.Empty;
        if (zoneId.Length is < 1 or > 128 || zoneId.Any(char.IsControl) ||
            from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero ||
            !SupportedBucketMinutes.Contains(bucketMinutes) || to <= from ||
            to - from > TimeSpan.FromHours(24) ||
            from.Ticks % TimeSpan.TicksPerMinute != 0 ||
            to.Ticks % TimeSpan.TicksPerMinute != 0 ||
            from.Minute % bucketMinutes != 0 || to.Minute % bucketMinutes != 0 ||
            (to - from).TotalMinutes % bucketMinutes != 0)
            throw new ArgumentException("Capacity projection requires an aligned UTC range up to 24 hours.");

        const string zoneSql = """
            SELECT EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = @zone_id);
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var zoneCommand = new NpgsqlCommand(zoneSql, connection))
        {
            zoneCommand.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            if (!(bool)(await zoneCommand.ExecuteScalarAsync(cancellationToken) ?? false))
                throw new ArgumentException("The requested zone is not active.", nameof(zoneId));
        }

        const string sql = """
            SELECT bucket_start,
                COUNT(DISTINCT courier.courier_id)::integer AS planned_courier_count
            FROM generate_series(
                @from,
                @to - make_interval(mins => @bucket_minutes),
                make_interval(mins => @bucket_minutes)) AS series(bucket_start)
            LEFT JOIN courier_availability_windows window
              ON window.zone_id = @zone_id
             AND window.status = 'SCHEDULED'
             AND window.starts_at < series.bucket_start + make_interval(mins => @bucket_minutes)
             AND window.ends_at > series.bucket_start
            LEFT JOIN couriers courier
              ON courier.courier_id = window.courier_id
             AND courier.status = 'ACTIVE'
            GROUP BY bucket_start
            ORDER BY bucket_start;
            """;
        var buckets = new List<CourierCapacityBucket>();
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from);
            command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to);
            command.Parameters.AddWithValue("bucket_minutes", NpgsqlDbType.Integer, bucketMinutes);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var bucketFrom = reader.GetFieldValue<DateTimeOffset>(0);
                buckets.Add(new CourierCapacityBucket(
                    bucketFrom, bucketFrom.AddMinutes(bucketMinutes), reader.GetInt32(1)));
            }
        }

        // The range and bucket limits cap this value at 96 for the audit ledger.
        await readAudit.RecordAsync(
            PostgresCourierOperationsReadAuditService.CourierCapacityProjection,
            null, buckets.Count, cancellationToken);
        return new CourierCapacityProjection(zoneId, bucketMinutes, buckets);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }
}
