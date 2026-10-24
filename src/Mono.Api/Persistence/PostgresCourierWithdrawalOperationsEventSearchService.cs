using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierWithdrawalOperationsEvent(long EventSequence, string WithdrawalId,
    string CourierId, string Action, string ActorType, string ActorId, DateTimeOffset OccurredAt,
    string? ReasonCode, string? BankReference);
public sealed record CourierWithdrawalOperationsEventSearchPage(
    IReadOnlyList<CourierWithdrawalOperationsEvent> Items, long? NextSequence);

public sealed class PostgresCourierWithdrawalOperationsEventSearchService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    public async Task<CourierWithdrawalOperationsEventSearchPage> SearchAsync(
        int pageSize, string? actionFilter, string? actorTypeFilter, string? actorIdFilter,
        string? courierIdFilter, DateTimeOffset? fromAt, DateTimeOffset? toAt,
        long? beforeSequence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        const string sql = """
            SELECT event.withdrawal_event_sequence, event.withdrawal_id, request.courier_id,
                   event.action, event.actor_type, event.actor_id, event.occurred_at,
                   event.reason_code, event.bank_reference
            FROM courier_withdrawal_events event
            JOIN courier_withdrawal_requests request USING (withdrawal_id)
            WHERE (@action IS NULL OR event.action = @action)
              AND (@actor_type IS NULL OR event.actor_type = @actor_type)
              AND (@actor_id IS NULL OR event.actor_id = @actor_id)
              AND (@courier_id IS NULL OR request.courier_id = @courier_id)
              AND (@from_at IS NULL OR event.occurred_at >= @from_at)
              AND (@to_at IS NULL OR event.occurred_at < @to_at)
              AND (@before_sequence IS NULL OR event.withdrawal_event_sequence < @before_sequence)
            ORDER BY event.withdrawal_event_sequence DESC
            LIMIT @take;
            """;
        var items = new List<CourierWithdrawalOperationsEvent>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.Add("action", NpgsqlDbType.Text).Value = actionFilter is null ? DBNull.Value : actionFilter;
            command.Parameters.Add("actor_type", NpgsqlDbType.Text).Value = actorTypeFilter is null ? DBNull.Value : actorTypeFilter;
            command.Parameters.Add("actor_id", NpgsqlDbType.Text).Value = actorIdFilter is null ? DBNull.Value : actorIdFilter;
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierIdFilter is null ? DBNull.Value : courierIdFilter;
            command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value = fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value;
            command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value = toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value;
            command.Parameters.Add("before_sequence", NpgsqlDbType.Bigint).Value = beforeSequence.HasValue ? beforeSequence.Value : DBNull.Value;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(pageSize + 1));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierWithdrawalOperationsEvent(reader.GetInt64(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
        var more = items.Count > pageSize;
        if (more) items.RemoveAt(pageSize);
        var next = more ? items[^1].EventSequence : (long?)null;
        await RecordReadAsync(connection, transaction, actionFilter, actorTypeFilter, actorIdFilter,
            courierIdFilter, fromAt, toAt, beforeSequence, pageSize, items.Count, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierWithdrawalOperationsEventSearchPage(items, next);
    }

    private async Task RecordReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string? action, string? actorType, string? actorId, string? courierId,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, long? cursor,
        int pageSize, int count, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_withdrawal_operations_event_search_audit
                (read_audit_id, actor_id, action_filter, actor_type_filter, actor_id_filter,
                 courier_id_filter, occurred_from_filter, occurred_to_filter, cursor_sequence,
                 page_size, result_count, outcome_code, occurred_at)
            VALUES (@id, @actor_id, @action, @actor_type, @actor_id_filter, @courier_id,
                    @from_at, @to_at, @cursor, @page_size, @count, @outcome, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cwe_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId);
        command.Parameters.Add("action", NpgsqlDbType.Text).Value = action is null ? DBNull.Value : action;
        command.Parameters.Add("actor_type", NpgsqlDbType.Text).Value = actorType is null ? DBNull.Value : actorType;
        command.Parameters.Add("actor_id_filter", NpgsqlDbType.Text).Value = actorId is null ? DBNull.Value : actorId;
        command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
        command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value = fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value;
        command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value = toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value;
        command.Parameters.Add("cursor", NpgsqlDbType.Bigint).Value = cursor.HasValue ? cursor.Value : DBNull.Value;
        command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
        command.Parameters.AddWithValue("count", NpgsqlDbType.Integer, count);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, count == 0 ? "EMPTY" : "FOUND");
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
