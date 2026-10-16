using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutDestinationAuditEntry(
    string EventKey,
    string EventType,
    string Action,
    string ActorId,
    DateTimeOffset OccurredAt,
    string? EvidenceReference,
    string? OutcomeCode,
    string? MaskedIban);

public sealed record CourierPayoutDestinationAuditCursor(DateTimeOffset OccurredAt, string EventKey);
public sealed record CourierPayoutDestinationAuditPage(
    string OutcomeCode,
    IReadOnlyList<CourierPayoutDestinationAuditEntry> Items,
    CourierPayoutDestinationAuditCursor? NextCursor);

public sealed class PostgresCourierPayoutDestinationAuditReadService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    public async Task<CourierPayoutDestinationAuditPage> ReadAsync(
        string courierId,
        int pageSize,
        CourierPayoutDestinationAuditCursor? cursor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!IsCourierId(courierId) || pageSize is < 1 or > 100)
            return new CourierPayoutDestinationAuditPage("INVALID_INPUT", [], null);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string courierSql = "SELECT EXISTS (SELECT 1 FROM couriers WHERE courier_id = @courier_id);";
        bool found;
        await using (var command = new NpgsqlCommand(courierSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            found = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }

        var items = new List<CourierPayoutDestinationAuditEntry>();
        CourierPayoutDestinationAuditCursor? nextCursor = null;
        if (found)
        {
            const string sql = """
                WITH audit_events AS (
                    SELECT e.courier_id,
                           'DESTINATION'::text AS event_type,
                           'd:' || lpad(e.destination_event_sequence::text, 20, '0') AS event_key,
                           e.action,
                           e.actor_id,
                           e.occurred_at,
                           e.evidence_reference,
                           NULL::text AS outcome_code,
                           destination.masked_iban
                    FROM courier_payout_destination_events e
                    JOIN courier_payout_destinations destination
                      ON destination.destination_id = e.destination_id
                    UNION ALL
                    SELECT a.courier_id,
                           'SECRET_ACCESS'::text AS event_type,
                           'a:' || a.access_audit_id AS event_key,
                           'SECRET_READ'::text AS action,
                           a.actor_id,
                           a.occurred_at,
                           NULL::text AS evidence_reference,
                           a.outcome_code,
                           destination.masked_iban
                    FROM courier_payout_destination_access_audit a
                    LEFT JOIN courier_payout_destinations destination
                      ON destination.destination_id = a.destination_id
                )
                SELECT event_key, event_type, action, actor_id, occurred_at,
                       evidence_reference, outcome_code, masked_iban
                FROM audit_events
                WHERE courier_id = @courier_id
                  AND (@before_at IS NULL OR
                       (occurred_at, event_key) < (@before_at, @before_key))
                ORDER BY occurred_at DESC, event_key DESC
                LIMIT @limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value =
                cursor is null ? DBNull.Value : cursor.OccurredAt;
            command.Parameters.Add("before_key", NpgsqlDbType.Text).Value =
                cursor is null ? DBNull.Value : cursor.EventKey;
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, pageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierPayoutDestinationAuditEntry(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        if (items.Count > pageSize)
        {
            var lastReturned = items[pageSize - 1];
            nextCursor = new CourierPayoutDestinationAuditCursor(lastReturned.OccurredAt, lastReturned.EventKey);
            items.RemoveAt(pageSize);
        }

        const string auditSql = """
            INSERT INTO courier_payout_destination_history_read_audit
                (read_audit_id, courier_id, actor_id, outcome_code, result_count, occurred_at)
            VALUES (@read_audit_id, @courier_id, @actor_id, @outcome_code, @result_count, @occurred_at);
            """;
        await using (var command = new NpgsqlCommand(auditSql, connection, transaction))
        {
            command.Parameters.AddWithValue("read_audit_id", NpgsqlDbType.Text, "cph_" + Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId);
            command.Parameters.AddWithValue("outcome_code", NpgsqlDbType.Text, found ? "FOUND" : "NOT_FOUND");
            command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, items.Count);
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutDestinationAuditPage(found ? "FOUND" : "NOT_FOUND", items, nextCursor);
    }

    private static bool IsCourierId(string value) =>
        value.Length == 36 && value.StartsWith("crr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);
}
