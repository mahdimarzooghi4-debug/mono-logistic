using System.Globalization;
using System.Text;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Services;

/// <summary>
/// Reads the append-only human review and activation history for an exact immutable geometry version.
/// </summary>
public sealed class MonoZoneGeometryHistoryService(
    NpgsqlDataSource dataSource,
    PostgresZoneGeometryRepository geometryRepository,
    IMonoOperationsContext operationsContext)
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;

    public async Task<ZoneGeometryHistoryPage> GetAsync(
        string zoneId,
        string geometryVersion,
        int? pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId));
        geometryVersion = Required(geometryVersion, nameof(geometryVersion));

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Page size must be between 1 and {MaximumPageSize}.");

        var after = DecodeCursor(cursor);
        var boundary = await geometryRepository.FindAsync(zoneId, geometryVersion, cancellationToken);
        if (boundary is null)
            throw new KeyNotFoundException("Zone geometry version was not found.");

        string snapshotHash;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand("""
            SELECT snapshot_sha256
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @geometry_version;
            """, connection))
        {
            command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            snapshotHash = (string?)await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Validated zone geometry snapshot disappeared.");
        }

        var rows = new List<HistoryRow>(size + 1);
        const string sql = """
            SELECT event_at, event_type, event_id, action, actor_id, reason, snapshot_sha256
            FROM (
                SELECT occurred_at AS event_at, 'REVIEW'::text AS event_type,
                       review_id AS event_id, decision AS action, actor_id, reason, snapshot_sha256
                FROM zone_geometry_reviews
                WHERE zone_id = @zone_id AND geometry_version = @geometry_version
                UNION ALL
                SELECT occurred_at AS event_at, 'ACTIVATION'::text AS event_type,
                       activation_event_id AS event_id, action, actor_id, reason, snapshot_sha256
                FROM zone_geometry_activation_events
                WHERE zone_id = @zone_id AND geometry_version = @geometry_version
            ) history
            WHERE (@after_at IS NULL OR
                   (event_at, event_type, event_id) > (@after_at, @after_type, @after_id))
            ORDER BY event_at, event_type, event_id
            LIMIT @take;
            """;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            command.Parameters.AddWithValue("after_at", NpgsqlDbType.TimestampTz,
                after is null ? DBNull.Value : after.Value.OccurredAt);
            command.Parameters.AddWithValue("after_type", NpgsqlDbType.Text,
                after is null ? DBNull.Value : after.Value.EventType);
            command.Parameters.AddWithValue("after_id", NpgsqlDbType.Bigint,
                after is null ? DBNull.Value : after.Value.EventId);
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, size + 1);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new HistoryRow(
                    reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1),
                    reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6)));
            }
        }

        foreach (var row in rows)
        {
            if (!string.Equals(row.SnapshotHash, snapshotHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Zone geometry lifecycle event does not match the immutable snapshot hash.");
        }

        var hasMore = rows.Count > size;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var events = rows.Select(row => new ZoneGeometryLifecycleEvent(
            row.OccurredAt, row.EventType, row.EventId, row.Action,
            row.ActorId, row.Reason, row.SnapshotHash)).ToArray();
        var nextCursor = hasMore && rows.Count > 0 ? EncodeCursor(rows[^1]) : null;

        return new ZoneGeometryHistoryPage(
            new ZoneGeometryHistorySnapshot(
                boundary.ZoneId, boundary.Kind.ToString(), boundary.GeometryVersion,
                boundary.SourceId, boundary.SourceRevision, boundary.Attribution,
                boundary.GeometryType, snapshotHash),
            events, hasMore, nextCursor);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }

    private static (DateTimeOffset OccurredAt, string EventType, long EventId)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        if (cursor.Length > 1024)
            throw new ArgumentException("History cursor is invalid.", nameof(cursor));

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = decoded.Split('\n');
            if (parts.Length != 3 ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var eventId) ||
                eventId < 1)
                throw new FormatException();

            var occurredAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (parts[1] is not "REVIEW" and not "ACTIVATION")
                throw new FormatException();
            return (occurredAt, parts[1], eventId);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("History cursor is invalid.", nameof(cursor));
        }
    }

    private static string EncodeCursor(HistoryRow row)
    {
        var value = string.Join('\n',
            row.OccurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            row.EventType,
            row.EventId.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed record HistoryRow(
        DateTimeOffset OccurredAt,
        string EventType,
        long EventId,
        string Action,
        string ActorId,
        string Reason,
        string SnapshotHash);
}

public sealed record ZoneGeometryHistoryPage(
    ZoneGeometryHistorySnapshot Snapshot,
    IReadOnlyList<ZoneGeometryLifecycleEvent> Events,
    bool HasMore,
    string? NextCursor);

public sealed record ZoneGeometryHistorySnapshot(
    string ZoneId,
    string Kind,
    string GeometryVersion,
    string SourceId,
    string SourceRevision,
    string Attribution,
    string GeometryType,
    string SnapshotHash);

public sealed record ZoneGeometryLifecycleEvent(
    DateTimeOffset OccurredAt,
    string EventType,
    long EventId,
    string Action,
    string ActorId,
    string Reason,
    string SnapshotHash);
