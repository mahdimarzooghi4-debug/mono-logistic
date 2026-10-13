using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Provides an audited, paginated view of missions waiting for courier dispatch.</summary>
public sealed class PostgresMissionDispatchQueueService(NpgsqlDataSource dataSource)
{
    public async Task<MissionDispatchQueuePage?> ListAsync(
        int pageSize, bool? hasOffer, string? query, string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100 ||
            !MissionOperationsInputValidator.IsValidQuery(query) ||
            !MissionOperationsInputValidator.IsValidCursor(cursor))
            throw new ArgumentException("Dispatch queue filters are invalid.");

        var normalizedQuery = query?.ToLowerInvariant();
        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            const string cursorSql = """
                SELECT m.created_at, (o.offer_id IS NOT NULL)
                FROM missions m
                LEFT JOIN mission_courier_offers o ON o.mission_id = m.mission_id AND o.status = 'OFFERED'
                WHERE m.mission_id = @mission_id AND m.status = 'SEARCHING_COURIER'
                  AND (@has_offer IS NULL OR (o.offer_id IS NOT NULL) = @has_offer)
                  AND (@query_pattern IS NULL
                       OR lower(m.mission_id) LIKE @query_pattern ESCAPE '!'
                       OR lower(m.delivery_request_id) LIKE @query_pattern ESCAPE '!')

                """;
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(cursorSql, connection);
            AddFilterParameters(command, hasOffer, normalizedQuery);
            command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, cursor);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            cursorCreatedAt = reader.GetFieldValue<DateTimeOffset>(0);
        }

        const string firstPageSql = """
            SELECT m.mission_id, m.customer_id, m.delivery_request_id, m.committed_delivery_at, m.created_at,
                   o.offer_id, o.courier_id, o.zone_id, o.status, o.created_at
            FROM missions m
            LEFT JOIN mission_courier_offers o ON o.mission_id = m.mission_id AND o.status = 'OFFERED'
            WHERE m.status = 'SEARCHING_COURIER'
              AND (@has_offer IS NULL OR (o.offer_id IS NOT NULL) = @has_offer)
              AND (@query_pattern IS NULL
                   OR lower(m.mission_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(m.delivery_request_id) LIKE @query_pattern ESCAPE '!')

            ORDER BY m.created_at DESC, m.mission_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT m.mission_id, m.customer_id, m.delivery_request_id, m.committed_delivery_at, m.created_at,
                   o.offer_id, o.courier_id, o.zone_id, o.status, o.created_at
            FROM missions m
            LEFT JOIN mission_courier_offers o ON o.mission_id = m.mission_id AND o.status = 'OFFERED'
            WHERE m.status = 'SEARCHING_COURIER'
              AND (@has_offer IS NULL OR (o.offer_id IS NOT NULL) = @has_offer)
              AND (@query_pattern IS NULL
                   OR lower(m.mission_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(m.delivery_request_id) LIKE @query_pattern ESCAPE '!')

              AND (m.created_at < @cursor_created_at
                   OR (m.created_at = @cursor_created_at AND m.mission_id < @cursor_id))
            ORDER BY m.created_at DESC, m.mission_id DESC
            LIMIT @take;
            """;

        await using var pageConnection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var pageCommand = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, pageConnection);
        AddFilterParameters(pageCommand, hasOffer, normalizedQuery);
        pageCommand.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            pageCommand.Parameters.AddWithValue("cursor_created_at", NpgsqlDbType.TimestampTz, cursorCreatedAt!.Value);
            pageCommand.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var items = new List<MissionDispatchQueueItem>(pageSize + 1);
        await using (var reader = await pageCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                MissionDispatchQueueOffer? activeOffer = reader.IsDBNull(6)
                    ? null
                    : new MissionDispatchQueueOffer(
                        reader.GetString(5), reader.GetString(6), reader.GetString(7),
                        reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9));
                items.Add(new MissionDispatchQueueItem(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4), activeOffer));
            }
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].MissionId;
        }
        return new MissionDispatchQueuePage(items, nextCursor);
    }

    private static void AddFilterParameters(NpgsqlCommand command, bool? hasOffer, string? query)
    {
        command.Parameters.AddWithValue("has_offer", NpgsqlDbType.Boolean, (object?)hasOffer ?? DBNull.Value);
        command.Parameters.AddWithValue("query_pattern", NpgsqlDbType.Text,
            query is null ? DBNull.Value : BuildPrefixPattern(query));
    }

    private static string BuildPrefixPattern(string query) =>
        query.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal) + "%";
}
