using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutReconciliationCursor(DateTimeOffset CreatedAt, string WithdrawalId);
public sealed record CourierPayoutReconciliationItem(
    string WithdrawalId, string CourierId, long AmountToman, string LatestAction,
    DateTimeOffset CreatedAt, long PayoutTransactionCount, long LedgerEntryCount,
    long MatchingCourierDebitCount, long MatchingClearingCreditCount, string ReconciliationCode);
public sealed record CourierPayoutReconciliationPage(
    IReadOnlyList<CourierPayoutReconciliationItem> Items, CourierPayoutReconciliationCursor? NextCursor);

public sealed class PostgresCourierPayoutReconciliationService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    public async Task<CourierPayoutReconciliationPage> ReadAsync(
        int pageSize, string? courierId, string? status, DateTimeOffset fromAt, DateTimeOffset toAt,
        bool onlyDiscrepancies, CourierPayoutReconciliationCursor? before,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        if (pageSize is < 1 or > 100 || fromAt >= toAt)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        var sql = CourierPayoutReconciliationSql.CommonCtes + """
            SELECT withdrawal_id, courier_id, amount_toman, latest_action, created_at,
                   transaction_count, entry_count, courier_debit_count, clearing_credit_count,
                   reconciliation_code
            FROM reconciled
            WHERE (NOT @only_discrepancies OR reconciliation_code <> 'MATCHED')
              AND (@before_created IS NULL OR (created_at, withdrawal_id) < (@before_created, @before_id))
            ORDER BY created_at DESC, withdrawal_id DESC
            LIMIT @take;
            """;
        var items = new List<CourierPayoutReconciliationItem>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value = fromAt.ToUniversalTime();
            command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value = toAt.ToUniversalTime();
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
            command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            command.Parameters.AddWithValue("only_discrepancies", NpgsqlDbType.Boolean, onlyDiscrepancies);
            command.Parameters.Add("before_created", NpgsqlDbType.TimestampTz).Value = before is null ? DBNull.Value : before.CreatedAt.ToUniversalTime();
            command.Parameters.Add("before_id", NpgsqlDbType.Text).Value = before is null ? DBNull.Value : before.WithdrawalId;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(pageSize + 1));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierPayoutReconciliationItem(
                    reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt64(5), reader.GetInt64(6),
                    reader.GetInt64(7), reader.GetInt64(8), reader.GetString(9)));
        }
        var more = items.Count > pageSize;
        if (more) items.RemoveAt(pageSize);
        var next = more ? new CourierPayoutReconciliationCursor(items[^1].CreatedAt, items[^1].WithdrawalId) : null;
        var outcome = items.Count == 0 ? "EMPTY" : "FOUND";
        await RecordReadAsync(connection, transaction, courierId, status, fromAt, toAt,
            onlyDiscrepancies, before, pageSize, items.Count, outcome, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutReconciliationPage(items, next);
    }

    private async Task RecordReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string? courierId, string? status, DateTimeOffset fromAt, DateTimeOffset toAt,
        bool onlyDiscrepancies, CourierPayoutReconciliationCursor? cursor,
        int pageSize, int resultCount, string outcome, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_payout_reconciliation_read_audit
                (read_audit_id, actor_id, courier_id_filter, status_filter, created_from_filter, created_to_filter,
                 only_discrepancies, cursor_created_at, cursor_withdrawal_id, page_size, result_count,
                 outcome_code, occurred_at)
            VALUES (@id, @actor_id, @courier_id, @status, @from_at, @to_at,
                    @only, @cursor_at, @cursor_id, @page_size, @count, @outcome, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cpr_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId);
        command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
        command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
        command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
        command.Parameters.AddWithValue("only", NpgsqlDbType.Boolean, onlyDiscrepancies);
        command.Parameters.Add("cursor_at", NpgsqlDbType.TimestampTz).Value = cursor is null ? DBNull.Value : cursor.CreatedAt.ToUniversalTime();
        command.Parameters.Add("cursor_id", NpgsqlDbType.Text).Value = cursor is null ? DBNull.Value : cursor.WithdrawalId;
        command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
        command.Parameters.AddWithValue("count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
