using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresMissionOperationsService(NpgsqlDataSource dataSource)
{
    private static readonly string[] TimelineEventTypes =
    [
        "mission.created", "mission.promised", "mission.searching_courier",
        "mission.courier_assigned", "mission.en_route_to_pickup", "mission.arrived_pickup",
        "mission.handed_to_courier", "mission.picked_up", "mission.in_transit",
        "mission.arrived_dropoff", "mission.delivered", "mission.completed",
        "mission.settlement_pending", "mission.settled", "mission.failed", "mission.cancelled"
    ];

    public async Task<MissionOperationsPage?> ListAsync(
        int pageSize,
        string? status,
        string? query,
        DateTimeOffset? createdAfter,
        DateTimeOffset? createdBefore,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (!MissionOperationsInputValidator.IsValidStatus(status) ||
            !MissionOperationsInputValidator.IsValidQuery(query) ||
            !MissionOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore))
            throw new ArgumentException("The Mission search filters are invalid.");

        var normalizedQuery = query?.ToLowerInvariant();
        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            const string cursorSql = """
                SELECT m.created_at, m.status, m.delivery_request_id, d.external_reference
                FROM missions m
                JOIN delivery_requests d
                  ON d.customer_id = m.customer_id AND d.delivery_request_id = m.delivery_request_id
                WHERE m.mission_id = @mission_id;
                """;
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand(cursorSql, cursorConnection);
            cursorCommand.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, cursor);
            await using var cursorReader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await cursorReader.ReadAsync(cancellationToken))
                return null;

            cursorCreatedAt = cursorReader.GetFieldValue<DateTimeOffset>(0);
            var cursorStatus = cursorReader.GetString(1);
            var cursorRequestId = cursorReader.GetString(2);
            var cursorExternalReference = cursorReader.GetString(3);
            if ((status is not null && !string.Equals(cursorStatus, status, StringComparison.Ordinal)) ||
                (createdAfter is not null && cursorCreatedAt < createdAfter) ||
                (createdBefore is not null && cursorCreatedAt > createdBefore) ||
                (normalizedQuery is not null && !MatchesQuery(
                    cursor, cursorRequestId, cursorExternalReference, normalizedQuery)))
                return null;
        }

        const string firstPageSql = """
            SELECT m.mission_id, m.customer_id, m.delivery_request_id, m.status,
                   m.committed_delivery_at, m.created_at
            FROM missions m
            JOIN delivery_requests d
              ON d.customer_id = m.customer_id AND d.delivery_request_id = m.delivery_request_id
            WHERE (@status IS NULL OR m.status = @status)
              AND (@query_pattern IS NULL
                   OR lower(m.mission_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(m.delivery_request_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(d.external_reference) LIKE @query_pattern ESCAPE '!')
              AND (@created_after IS NULL OR m.created_at >= @created_after)
              AND (@created_before IS NULL OR m.created_at <= @created_before)
            ORDER BY m.created_at DESC, m.mission_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT m.mission_id, m.customer_id, m.delivery_request_id, m.status,
                   m.committed_delivery_at, m.created_at
            FROM missions m
            JOIN delivery_requests d
              ON d.customer_id = m.customer_id AND d.delivery_request_id = m.delivery_request_id
            WHERE (@status IS NULL OR m.status = @status)
              AND (@query_pattern IS NULL
                   OR lower(m.mission_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(m.delivery_request_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(d.external_reference) LIKE @query_pattern ESCAPE '!')
              AND (@created_after IS NULL OR m.created_at >= @created_after)
              AND (@created_before IS NULL OR m.created_at <= @created_before)
              AND (m.created_at < @cursor_created_at
                   OR (m.created_at = @cursor_created_at AND m.mission_id < @cursor_id))
            ORDER BY m.created_at DESC, m.mission_id DESC
            LIMIT @take;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, connection);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("query_pattern", NpgsqlDbType.Text,
            normalizedQuery is null ? DBNull.Value : BuildPrefixPattern(normalizedQuery));
        command.Parameters.AddWithValue("created_after", NpgsqlDbType.TimestampTz,
            (object?)createdAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("created_before", NpgsqlDbType.TimestampTz,
            (object?)createdBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("cursor_created_at", NpgsqlDbType.TimestampTz, cursorCreatedAt!.Value);
            command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var items = new List<MissionOperationsItem>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadMission(reader));
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].MissionId;
        }
        return new MissionOperationsPage(items, nextCursor);
    }

    public async Task<MissionOperationsItem?> FindByIdAsync(
        string missionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT mission_id, customer_id, delivery_request_id, status, committed_delivery_at, created_at
            FROM missions WHERE mission_id = @mission_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMission(reader) : null;
    }

    public async Task<MissionTimelineResponse?> FindTimelineAsync(
        string missionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT m.mission_id, e.event_id, e.event_type, e.sequence, e.occurred_at
            FROM missions m
            LEFT JOIN outbox_events e
              ON e.customer_id = m.customer_id
             AND e.aggregate_type = 'mission'
             AND e.aggregate_id = m.mission_id
             AND e.event_type = ANY(@event_types)
            WHERE m.mission_id = @mission_id
            ORDER BY e.sequence ASC;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        command.Parameters.AddWithValue("event_types", NpgsqlDbType.Array | NpgsqlDbType.Text, TimelineEventTypes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        string? foundMissionId = null;
        var events = new List<MissionTimelineEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            foundMissionId ??= reader.GetString(0);
            if (!reader.IsDBNull(1))
                events.Add(new MissionTimelineEvent(reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return foundMissionId is null ? null : new MissionTimelineResponse(foundMissionId, events);
    }

    private static string BuildPrefixPattern(string query) =>
        query.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal) + "%";

    private static bool MatchesQuery(
        string missionId, string deliveryRequestId, string externalReference, string normalizedQuery) =>
        missionId.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
        deliveryRequestId.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
        externalReference.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase);

    private static MissionOperationsItem ReadMission(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
}
