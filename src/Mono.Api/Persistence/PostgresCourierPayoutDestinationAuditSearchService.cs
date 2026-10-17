using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresCourierPayoutDestinationAuditSearchService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    public async Task<CourierPayoutDestinationAuditPage> SearchAsync(
        int pageSize, string? courierId, string? eventType, string? action, string? actorId,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, CourierPayoutDestinationAuditCursor? before,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        const string sql = """
            WITH audit_events AS (
                SELECT e.courier_id, 'DESTINATION'::text AS event_type,
                       'd:' || lpad(e.destination_event_sequence::text, 20, '0') AS event_key,
                       e.action, e.actor_id, e.occurred_at, e.evidence_reference,
                       NULL::text AS outcome_code, destination.masked_iban
                FROM courier_payout_destination_events e
                JOIN courier_payout_destinations destination USING (destination_id)
                UNION ALL
                SELECT a.courier_id, 'SECRET_ACCESS'::text AS event_type,
                       'a:' || a.access_audit_id AS event_key, 'SECRET_READ'::text AS action,
                       a.actor_id, a.occurred_at, NULL::text AS evidence_reference,
                       a.outcome_code, destination.masked_iban
                FROM courier_payout_destination_access_audit a
                LEFT JOIN courier_payout_destinations destination USING (destination_id)
            )
            SELECT event_key, event_type, action, actor_id, occurred_at, evidence_reference, outcome_code, masked_iban
            FROM audit_events
            WHERE (@courier_id IS NULL OR courier_id = @courier_id)
              AND (@event_type IS NULL OR event_type = @event_type)
              AND (@action IS NULL OR action = @action)
              AND (@actor_id IS NULL OR actor_id = @actor_id)
              AND (@from_at IS NULL OR occurred_at >= @from_at)
              AND (@to_at IS NULL OR occurred_at < @to_at)
              AND (@before_at IS NULL OR (occurred_at, event_key) < (@before_at, @before_key))
            ORDER BY occurred_at DESC, event_key DESC
            LIMIT @take;
            """;
        var items = new List<CourierPayoutDestinationAuditEntry>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
            command.Parameters.Add("event_type", NpgsqlDbType.Text).Value = eventType is null ? DBNull.Value : eventType;
            command.Parameters.Add("action", NpgsqlDbType.Text).Value = action is null ? DBNull.Value : action;
            command.Parameters.Add("actor_id", NpgsqlDbType.Text).Value = actorId is null ? DBNull.Value : actorId;
            command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value = fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value;
            command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value = toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value;
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value = before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
            command.Parameters.Add("before_key", NpgsqlDbType.Text).Value = before is null ? DBNull.Value : before.EventKey;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(pageSize + 1));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierPayoutDestinationAuditEntry(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        var more = items.Count > pageSize;
        if (more) items.RemoveAt(pageSize);
        var next = more ? new CourierPayoutDestinationAuditCursor(items[^1].OccurredAt, items[^1].EventKey) : null;
        var outcome = items.Count == 0 ? "EMPTY" : "FOUND";
        await RecordReadAsync(connection, transaction, courierId, eventType, action, actorId,
            fromAt, toAt, before, pageSize, items.Count, outcome, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutDestinationAuditPage(outcome, items, next);
    }

    private async Task RecordReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string? courierId, string? eventType, string? action, string? actorId,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, CourierPayoutDestinationAuditCursor? cursor,
        int pageSize, int resultCount, string outcome, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_payout_destination_operations_search_audit
                (read_audit_id, actor_id, courier_id_filter, event_type_filter, action_filter, actor_id_filter,
                 occurred_from_filter, occurred_to_filter, cursor_occurred_at, cursor_event_key,
                 page_size, result_count, outcome_code, occurred_at)
            VALUES (@id, @actor_id, @courier_id, @event_type, @action, @actor_id_filter,
                    @from_at, @to_at, @cursor_at, @cursor_key, @page_size, @result_count, @outcome, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cps_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId);
        command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
        command.Parameters.Add("event_type", NpgsqlDbType.Text).Value = eventType is null ? DBNull.Value : eventType;
        command.Parameters.Add("action", NpgsqlDbType.Text).Value = action is null ? DBNull.Value : action;
        command.Parameters.Add("actor_id_filter", NpgsqlDbType.Text).Value = actorId is null ? DBNull.Value : actorId;
        command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value = fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value;
        command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value = toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value;
        command.Parameters.Add("cursor_at", NpgsqlDbType.TimestampTz).Value = cursor is null ? DBNull.Value : cursor.OccurredAt.ToUniversalTime();
        command.Parameters.Add("cursor_key", NpgsqlDbType.Text).Value = cursor is null ? DBNull.Value : cursor.EventKey;
        command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
