using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierWithdrawalHistoryEvent(
    long EventSequence, string Action, string ActorType, string ActorId, DateTimeOffset OccurredAt,
    string? ReasonCode, string? BankReference, string IdempotencyKey);

public sealed record CourierWithdrawalHistoryReadResult(
    string OutcomeCode, string WithdrawalId, IReadOnlyList<CourierWithdrawalHistoryEvent> Events);

public sealed class PostgresCourierWithdrawalHistoryReadService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    public async Task<CourierWithdrawalHistoryReadResult> ReadAsync(
        string withdrawalId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IsValidWithdrawalId(withdrawalId))
            return new CourierWithdrawalHistoryReadResult("NOT_FOUND", withdrawalId, []);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string existsSql = """
            SELECT EXISTS (SELECT 1 FROM courier_withdrawal_requests WHERE withdrawal_id = @withdrawal_id);
            """;
        bool found;
        await using (var command = new NpgsqlCommand(existsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            found = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }

        var events = new List<CourierWithdrawalHistoryEvent>();
        if (found)
        {
            const string eventsSql = """
                SELECT withdrawal_event_sequence, action, actor_type, actor_id, occurred_at,
                       reason_code, bank_reference, idempotency_key
                FROM courier_withdrawal_events
                WHERE withdrawal_id = @withdrawal_id
                ORDER BY withdrawal_event_sequence ASC;
                """;
            await using var command = new NpgsqlCommand(eventsSql, connection, transaction);
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                events.Add(new CourierWithdrawalHistoryEvent(reader.GetInt64(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7)));
        }

        const string auditSql = """
            INSERT INTO courier_withdrawal_history_read_audit
                (read_audit_id, withdrawal_id, actor_id, outcome_code, event_count, occurred_at)
            VALUES (@read_audit_id, @withdrawal_id, @actor_id, @outcome_code, @event_count, @occurred_at);
            """;
        await using (var command = new NpgsqlCommand(auditSql, connection, transaction))
        {
            command.Parameters.AddWithValue("read_audit_id", NpgsqlDbType.Text, "cwr_" + Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId);
            command.Parameters.AddWithValue("outcome_code", NpgsqlDbType.Text, found ? "FOUND" : "NOT_FOUND");
            command.Parameters.AddWithValue("event_count", NpgsqlDbType.Integer, events.Count);
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new CourierWithdrawalHistoryReadResult(found ? "FOUND" : "NOT_FOUND", withdrawalId, events);
    }

    private static bool IsValidWithdrawalId(string value) => value.Length == 36 &&
        value.StartsWith("wdr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}
