using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Provides an audited, paginated view of missions waiting for courier dispatch.</summary>
public sealed class PostgresMissionDispatchQueueService(
    NpgsqlDataSource dataSource, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public async Task<MissionDispatchQueuePage?> ListAsync(
        int pageSize, bool? hasOffer, string? query, string? cursor,
        CancellationToken cancellationToken = default, bool? deadlinePassed = null, string? latestAction = null,
        DateTimeOffset? deliveryBefore = null, int? minAttemptCount = null, int? maxAttemptCount = null)
    {
        if (pageSize is < 1 or > 100 ||
            !MissionOperationsInputValidator.IsValidQuery(query) ||
            !MissionOperationsInputValidator.IsValidCursor(cursor) ||
            !IsValidLatestAction(latestAction) ||
            (minAttemptCount.HasValue && minAttemptCount.Value < 0) ||
            (maxAttemptCount.HasValue && maxAttemptCount.Value < 0) ||
            (minAttemptCount.HasValue && maxAttemptCount.HasValue && minAttemptCount.Value > maxAttemptCount.Value))
            throw new ArgumentException("Dispatch queue filters are invalid.");

        var normalizedQuery = query?.ToLowerInvariant();
        var asOf = _timeProvider.GetUtcNow();
        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            const string cursorSql = """
                SELECT m.created_at, (o.offer_id IS NOT NULL)
                FROM missions m
                LEFT JOIN mission_courier_offers o ON o.mission_id = m.mission_id AND o.status = 'OFFERED'
                LEFT JOIN LATERAL (
                    SELECT count(*)::integer AS attempt_count
                    FROM mission_courier_offers previous
                    WHERE previous.mission_id = m.mission_id
                ) attempts ON TRUE
                WHERE m.mission_id = @mission_id AND m.status = 'SEARCHING_COURIER'
                  AND (@has_offer IS NULL OR (o.offer_id IS NOT NULL) = @has_offer)
                  AND (@deadline_passed IS NULL OR
                       (o.offer_id IS NOT NULL AND COALESCE(o.expires_at <= @as_of, FALSE) = @deadline_passed))

                  AND (@delivery_before IS NULL OR m.committed_delivery_at <= @delivery_before)
                  AND (@min_attempt_count IS NULL OR attempts.attempt_count >= @min_attempt_count)
                  AND (@max_attempt_count IS NULL OR attempts.attempt_count <= @max_attempt_count)
                  AND (@latest_action IS NULL OR (
                       SELECT latest.action
                       FROM mission_courier_offers previous
                       LEFT JOIN LATERAL (
                           SELECT event.action
                           FROM mission_courier_offer_events event
                           WHERE event.offer_id = previous.offer_id
                           ORDER BY event.event_sequence DESC
                           LIMIT 1
                       ) latest ON TRUE
                       WHERE previous.mission_id = m.mission_id
                       ORDER BY previous.created_at DESC, previous.offer_id DESC
                       LIMIT 1
                  ) = @latest_action)
                  AND (@query_pattern IS NULL
                       OR lower(m.mission_id) LIKE @query_pattern ESCAPE '!'
                       OR lower(m.delivery_request_id) LIKE @query_pattern ESCAPE '!')

                """;
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(cursorSql, connection);
            AddFilterParameters(command, hasOffer, normalizedQuery, deadlinePassed, latestAction, deliveryBefore, minAttemptCount, maxAttemptCount, asOf);
            command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, cursor);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            cursorCreatedAt = reader.GetFieldValue<DateTimeOffset>(0);
        }

        const string firstPageSql = """
            SELECT m.mission_id, m.customer_id, m.delivery_request_id, m.committed_delivery_at, m.created_at,
                   o.offer_id, o.courier_id, o.zone_id, o.status, o.created_at, o.expires_at,
                   attempts.attempt_count, latest_attempt.offer_id, latest_attempt.courier_id,
                   latest_attempt.status, latest_attempt.action, latest_attempt.reason, latest_attempt.occurred_at
            FROM missions m
            LEFT JOIN mission_courier_offers o ON o.mission_id = m.mission_id AND o.status = 'OFFERED'
            LEFT JOIN LATERAL (
                SELECT count(*)::integer AS attempt_count
                FROM mission_courier_offers previous
                WHERE previous.mission_id = m.mission_id
            ) attempts ON TRUE
            LEFT JOIN LATERAL (
                SELECT previous.offer_id, previous.courier_id, previous.status,
                       latest.action, latest.reason, latest.occurred_at
                FROM mission_courier_offers previous
                LEFT JOIN LATERAL (
                    SELECT event.action, event.reason, event.occurred_at
                    FROM mission_courier_offer_events event
                    WHERE event.offer_id = previous.offer_id
                    ORDER BY event.event_sequence DESC
                    LIMIT 1
                ) latest ON TRUE
                WHERE previous.mission_id = m.mission_id
                ORDER BY previous.created_at DESC, previous.offer_id DESC
                LIMIT 1
            ) latest_attempt ON TRUE
            WHERE m.status = 'SEARCHING_COURIER'
              AND (@has_offer IS NULL OR (o.offer_id IS NOT NULL) = @has_offer)
              AND (@deadline_passed IS NULL OR
                   (o.offer_id IS NOT NULL AND COALESCE(o.expires_at <= @as_of, FALSE) = @deadline_passed))

              AND (@delivery_before IS NULL OR m.committed_delivery_at <= @delivery_before)
              AND (@min_attempt_count IS NULL OR attempts.attempt_count >= @min_attempt_count)
              AND (@max_attempt_count IS NULL OR attempts.attempt_count <= @max_attempt_count)
              AND (@latest_action IS NULL OR (
                   SELECT latest.action
                   FROM mission_courier_offers previous
                   LEFT JOIN LATERAL (
                       SELECT event.action
                       FROM mission_courier_offer_events event
                       WHERE event.offer_id = previous.offer_id
                       ORDER BY event.event_sequence DESC
                       LIMIT 1
                   ) latest ON TRUE
                   WHERE previous.mission_id = m.mission_id
                   ORDER BY previous.created_at DESC, previous.offer_id DESC
                   LIMIT 1
              ) = @latest_action)
              AND (@query_pattern IS NULL
                   OR lower(m.mission_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(m.delivery_request_id) LIKE @query_pattern ESCAPE '!')

            ORDER BY m.created_at DESC, m.mission_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT m.mission_id, m.customer_id, m.delivery_request_id, m.committed_delivery_at, m.created_at,
                   o.offer_id, o.courier_id, o.zone_id, o.status, o.created_at, o.expires_at,
                   attempts.attempt_count, latest_attempt.offer_id, latest_attempt.courier_id,
                   latest_attempt.status, latest_attempt.action, latest_attempt.reason, latest_attempt.occurred_at
            FROM missions m
            LEFT JOIN mission_courier_offers o ON o.mission_id = m.mission_id AND o.status = 'OFFERED'
            LEFT JOIN LATERAL (
                SELECT count(*)::integer AS attempt_count
                FROM mission_courier_offers previous
                WHERE previous.mission_id = m.mission_id
            ) attempts ON TRUE
            LEFT JOIN LATERAL (
                SELECT previous.offer_id, previous.courier_id, previous.status,
                       latest.action, latest.reason, latest.occurred_at
                FROM mission_courier_offers previous
                LEFT JOIN LATERAL (
                    SELECT event.action, event.reason, event.occurred_at
                    FROM mission_courier_offer_events event
                    WHERE event.offer_id = previous.offer_id
                    ORDER BY event.event_sequence DESC
                    LIMIT 1
                ) latest ON TRUE
                WHERE previous.mission_id = m.mission_id
                ORDER BY previous.created_at DESC, previous.offer_id DESC
                LIMIT 1
            ) latest_attempt ON TRUE
            WHERE m.status = 'SEARCHING_COURIER'
              AND (@has_offer IS NULL OR (o.offer_id IS NOT NULL) = @has_offer)
              AND (@deadline_passed IS NULL OR
                   (o.offer_id IS NOT NULL AND COALESCE(o.expires_at <= @as_of, FALSE) = @deadline_passed))

              AND (@delivery_before IS NULL OR m.committed_delivery_at <= @delivery_before)
              AND (@min_attempt_count IS NULL OR attempts.attempt_count >= @min_attempt_count)
              AND (@max_attempt_count IS NULL OR attempts.attempt_count <= @max_attempt_count)
              AND (@latest_action IS NULL OR (
                   SELECT latest.action
                   FROM mission_courier_offers previous
                   LEFT JOIN LATERAL (
                       SELECT event.action
                       FROM mission_courier_offer_events event
                       WHERE event.offer_id = previous.offer_id
                       ORDER BY event.event_sequence DESC
                       LIMIT 1
                   ) latest ON TRUE
                   WHERE previous.mission_id = m.mission_id
                   ORDER BY previous.created_at DESC, previous.offer_id DESC
                   LIMIT 1
              ) = @latest_action)
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
        AddFilterParameters(pageCommand, hasOffer, normalizedQuery, deadlinePassed, latestAction, deliveryBefore, minAttemptCount, maxAttemptCount, asOf);
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
                        reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9),
                        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                        !reader.IsDBNull(10) && reader.GetFieldValue<DateTimeOffset>(10) <= _timeProvider.GetUtcNow());
                MissionDispatchQueueAttempt? latestAttempt = reader.IsDBNull(12)
                    ? null
                    : new MissionDispatchQueueAttempt(
                        reader.GetString(12), reader.GetString(13), reader.GetString(14),
                        reader.IsDBNull(15) ? null : reader.GetString(15),
                        reader.IsDBNull(16) ? null : reader.GetString(16),
                        reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17));
                items.Add(new MissionDispatchQueueItem(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4), activeOffer,
                    reader.GetInt32(11), latestAttempt));
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

    private static void AddFilterParameters(
        NpgsqlCommand command, bool? hasOffer, string? query, bool? deadlinePassed,
        string? latestAction, DateTimeOffset? deliveryBefore, int? minAttemptCount, int? maxAttemptCount,
        DateTimeOffset asOf)
    {
        command.Parameters.AddWithValue("has_offer", NpgsqlDbType.Boolean, (object?)hasOffer ?? DBNull.Value);
        command.Parameters.AddWithValue("query_pattern", NpgsqlDbType.Text,
            query is null ? DBNull.Value : BuildPrefixPattern(query));
        command.Parameters.AddWithValue("deadline_passed", NpgsqlDbType.Boolean,
            (object?)deadlinePassed ?? DBNull.Value);
        command.Parameters.AddWithValue("as_of", NpgsqlDbType.TimestampTz, asOf);
        command.Parameters.AddWithValue("latest_action", NpgsqlDbType.Text,
            latestAction is null ? DBNull.Value : latestAction);
        command.Parameters.AddWithValue("delivery_before", NpgsqlDbType.TimestampTz,
            (object?)deliveryBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("min_attempt_count", NpgsqlDbType.Integer,
            (object?)minAttemptCount ?? DBNull.Value);
        command.Parameters.AddWithValue("max_attempt_count", NpgsqlDbType.Integer,
            (object?)maxAttemptCount ?? DBNull.Value);
    }

    private static bool IsValidLatestAction(string? latestAction) =>
        latestAction is null or "OFFERED" or "ACCEPTED" or "DECLINED" or "WITHDRAWN" or
            "EXPIRED" or "RELEASED" or "CANCELLED" or "COMPLETED";

    private static string BuildPrefixPattern(string query) =>
        query.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal) + "%";
}
