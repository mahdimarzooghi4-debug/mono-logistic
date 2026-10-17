using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierWithdrawalOperationsAuditCursor(
    DateTimeOffset OccurredAt, string SearchAuditId);

public sealed record CourierWithdrawalOperationsAuditEntry(
    string SearchAuditId, string ActorId, string? StatusFilter, string? CourierIdFilter,
    DateTimeOffset? CreatedFromFilter, DateTimeOffset? CreatedToFilter,
    long? MinAmountTomanFilter, long? MaxAmountTomanFilter, long? CursorSequence,
    int PageSize, int ResultCount, DateTimeOffset OccurredAt);

public sealed record CourierWithdrawalOperationsAuditPage(
    IReadOnlyList<CourierWithdrawalOperationsAuditEntry> Items,
    CourierWithdrawalOperationsAuditCursor? Next);

public sealed class PostgresCourierWithdrawalOperationsAuditReadService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    public async Task<CourierWithdrawalOperationsAuditPage> ReadAsync(
        int pageSize, string? actorFilter, string? statusFilter, string? courierIdFilter,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, CourierWithdrawalOperationsAuditCursor? before,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        const string sql = """
            SELECT search_audit_id, actor_id, status_filter, courier_id_filter,
                   created_from_filter, created_to_filter, min_amount_toman_filter,
                   max_amount_toman_filter, cursor_sequence, page_size, result_count, occurred_at
            FROM courier_withdrawal_operations_search_audit
            WHERE (@actor_id IS NULL OR actor_id = @actor_id)
              AND (@status IS NULL OR status_filter = @status)
              AND (@courier_id IS NULL OR courier_id_filter = @courier_id)
              AND (@from_at IS NULL OR occurred_at >= @from_at)
              AND (@to_at IS NULL OR occurred_at < @to_at)
              AND (@before_at IS NULL OR (occurred_at, search_audit_id) < (@before_at, @before_id))
            ORDER BY occurred_at DESC, search_audit_id DESC
            LIMIT @take;
            """;
        var items = new List<CourierWithdrawalOperationsAuditEntry>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.Add("actor_id", NpgsqlDbType.Text).Value =
                actorFilter is null ? DBNull.Value : actorFilter;
            command.Parameters.Add("status", NpgsqlDbType.Text).Value =
                statusFilter is null ? DBNull.Value : statusFilter;
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value =
                courierIdFilter is null ? DBNull.Value : courierIdFilter;
            command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value =
                fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value;
            command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value =
                toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value;
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value =
                before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
            command.Parameters.Add("before_id", NpgsqlDbType.Text).Value =
                before is null ? DBNull.Value : before.SearchAuditId;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(pageSize + 1));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierWithdrawalOperationsAuditEntry(
                    reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.GetInt32(9), reader.GetInt32(10), reader.GetFieldValue<DateTimeOffset>(11)));
        }
        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(pageSize);
        var next = hasMore
            ? new CourierWithdrawalOperationsAuditCursor(items[^1].OccurredAt, items[^1].SearchAuditId)
            : null;
        await RecordReadAsync(connection, transaction, actorFilter, statusFilter, courierIdFilter,
            fromAt, toAt, before, pageSize, items.Count, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierWithdrawalOperationsAuditPage(items, next);
    }

    private async Task RecordReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string? actorFilter, string? statusFilter, string? courierIdFilter,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, CourierWithdrawalOperationsAuditCursor? before,
        int pageSize, int resultCount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_withdrawal_operations_audit_read
                (read_audit_id, actor_id, actor_filter, status_filter, courier_id_filter,
                 occurred_from_filter, occurred_to_filter, cursor_occurred_at, cursor_search_audit_id,
                 page_size, result_count, outcome_code, occurred_at)
            VALUES (@id, @actor_id, @actor_filter, @status, @courier_id,
                    @from_at, @to_at, @cursor_at, @cursor_id,
                    @page_size, @result_count, @outcome, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cwa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId);
        command.Parameters.Add("actor_filter", NpgsqlDbType.Text).Value =
            actorFilter is null ? DBNull.Value : actorFilter;
        command.Parameters.Add("status", NpgsqlDbType.Text).Value =
            statusFilter is null ? DBNull.Value : statusFilter;
        command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value =
            courierIdFilter is null ? DBNull.Value : courierIdFilter;
        command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value =
            fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value;
        command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value =
            toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value;
        command.Parameters.Add("cursor_at", NpgsqlDbType.TimestampTz).Value =
            before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
        command.Parameters.Add("cursor_id", NpgsqlDbType.Text).Value =
            before is null ? DBNull.Value : before.SearchAuditId;
        command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, resultCount == 0 ? "EMPTY" : "FOUND");
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
