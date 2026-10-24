using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierLivePresenceOperationsSearchItem(
    string CourierId, string Status, string? ZoneId, DateTimeOffset? UpdatedAt);

public sealed record CourierLivePresenceOperationsSearchPage(
    IReadOnlyList<CourierLivePresenceOperationsSearchItem> Items, string? NextCursor);

/// <summary>Read-only, administrator-scoped roster of current courier presence.</summary>
public sealed class PostgresCourierLivePresenceOperationsSearchService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresCourierOperationsReadAuditService readAudit)
{
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ZoneIdPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._~-]{0,127}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<CourierLivePresenceOperationsSearchPage> SearchAsync(
        int pageSize, string? status, string? zoneId, DateTimeOffset? updatedAfter,
        DateTimeOffset? updatedBefore, string? beforeCourierId,
        CancellationToken cancellationToken = default)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if (status is not (null or "ONLINE" or "OFFLINE"))
            throw new ArgumentException("Unknown courier presence status.", nameof(status));
        zoneId = string.IsNullOrWhiteSpace(zoneId) ? null : zoneId.Trim();
        if (zoneId is not null && !ZoneIdPattern.IsMatch(zoneId))
            throw new ArgumentException("A valid zone ID is required.", nameof(zoneId));
        if (updatedAfter.HasValue) updatedAfter = updatedAfter.Value.ToUniversalTime();
        if (updatedBefore.HasValue) updatedBefore = updatedBefore.Value.ToUniversalTime();
        if (updatedAfter.HasValue && updatedBefore.HasValue && updatedAfter >= updatedBefore)
            throw new ArgumentException("The presence time range is invalid.");
        if (beforeCourierId is not null && !CourierIdPattern.IsMatch(beforeCourierId))
            throw new ArgumentException("The courier cursor is invalid.", nameof(beforeCourierId));

        const string sql =
            "SELECT courier.courier_id, " +
            "CASE WHEN COALESCE(presence.is_online, FALSE) THEN 'ONLINE' ELSE 'OFFLINE' END, " +
            "presence.zone_id, presence.changed_at " +
            "FROM couriers courier " +
            "LEFT JOIN courier_live_presence presence ON presence.courier_id = courier.courier_id " +
            "WHERE (@status IS NULL OR COALESCE(presence.is_online, FALSE) = (@status = 'ONLINE')) " +
            "AND (@zone_id IS NULL OR presence.zone_id = @zone_id) " +
            "AND (@updated_after IS NULL OR presence.changed_at >= @updated_after) " +
            "AND (@updated_before IS NULL OR presence.changed_at < @updated_before) " +
            "AND (@before_id IS NULL OR courier.courier_id > @before_id) " +
            "ORDER BY courier.courier_id ASC LIMIT @take;";

        var items = new List<CourierLivePresenceOperationsSearchItem>(pageSize + 1);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            command.Parameters.Add("zone_id", NpgsqlDbType.Text).Value = zoneId is null ? DBNull.Value : zoneId;
            command.Parameters.Add("updated_after", NpgsqlDbType.TimestampTz).Value =
                updatedAfter.HasValue ? updatedAfter.Value : DBNull.Value;
            command.Parameters.Add("updated_before", NpgsqlDbType.TimestampTz).Value =
                updatedBefore.HasValue ? updatedBefore.Value : DBNull.Value;
            command.Parameters.Add("before_id", NpgsqlDbType.Text).Value =
                beforeCourierId is null ? DBNull.Value : beforeCourierId;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CourierLivePresenceOperationsSearchItem(
                    reader.GetString(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
            }
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].CourierId;
        }
        await readAudit.RecordAsync(
            PostgresCourierOperationsReadAuditService.CourierPresenceSearch,
            null, items.Count, cancellationToken);
        return new CourierLivePresenceOperationsSearchPage(items, nextCursor);
    }
}
