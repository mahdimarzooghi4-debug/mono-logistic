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
        const string sql = """
            WITH latest_event AS (
                SELECT DISTINCT ON (withdrawal_id) withdrawal_id, action
                FROM courier_withdrawal_events
                ORDER BY withdrawal_id, withdrawal_event_sequence DESC
            ),
            payout_ledger AS (
                SELECT payout_tx.reference_id AS withdrawal_id,
                       COUNT(DISTINCT payout_tx.ledger_transaction_id) AS transaction_count,
                       COUNT(entry.ledger_transaction_id) AS entry_count,
                       COUNT(entry.ledger_transaction_id) FILTER (
                           WHERE account.owner_type = 'COURIER'
                             AND account.owner_id = request.courier_id
                             AND account.account_code = 'COURIER_AVAILABLE'
                             AND account.currency = payout_tx.currency
                             AND payout_tx.currency = 'TOMAN'
                             AND entry.debit_amount = request.amount_toman
                             AND entry.credit_amount = 0) AS courier_debit_count,
                       COUNT(entry.ledger_transaction_id) FILTER (
                           WHERE account.owner_type = 'MONO'
                             AND account.owner_id = 'mono'
                             AND account.account_code = 'MONO_PAYOUT_CLEARING'
                             AND account.currency = payout_tx.currency
                             AND payout_tx.currency = 'TOMAN'
                             AND entry.debit_amount = 0
                             AND entry.credit_amount = request.amount_toman) AS clearing_credit_count
                FROM ledger_transactions payout_tx
                JOIN courier_withdrawal_requests request
                  ON request.withdrawal_id = payout_tx.reference_id
                LEFT JOIN ledger_entries entry
                  ON entry.ledger_transaction_id = payout_tx.ledger_transaction_id
                LEFT JOIN ledger_accounts account
                  ON account.ledger_account_id = entry.ledger_account_id
                WHERE payout_tx.reference_type = 'WITHDRAWAL_PAYOUT'
                GROUP BY payout_tx.reference_id, request.courier_id, request.amount_toman
            ),
            reconciled AS (
                SELECT request.withdrawal_id, request.courier_id, request.amount_toman,
                       latest.action AS latest_action, request.created_at,
                       COALESCE(payout.transaction_count, 0) AS transaction_count,
                       COALESCE(payout.entry_count, 0) AS entry_count,
                       COALESCE(payout.courier_debit_count, 0) AS courier_debit_count,
                       COALESCE(payout.clearing_credit_count, 0) AS clearing_credit_count,
                       CASE
                         WHEN COALESCE(payout.transaction_count, 0) > 1 THEN 'MULTIPLE_PAYOUTS'
                         WHEN latest.action = 'PAID' AND COALESCE(payout.transaction_count, 0) = 0 THEN 'MISSING_PAYOUT'
                         WHEN latest.action <> 'PAID' AND COALESCE(payout.transaction_count, 0) > 0 THEN 'UNEXPECTED_PAYOUT'
                         WHEN latest.action <> 'PAID' AND COALESCE(payout.transaction_count, 0) = 0 THEN 'MATCHED'
                         WHEN payout.transaction_count = 1 AND payout.entry_count = 2
                          AND payout.courier_debit_count = 1 AND payout.clearing_credit_count = 1 THEN 'MATCHED'
                         ELSE 'LEDGER_MISMATCH'
                       END AS reconciliation_code
                FROM courier_withdrawal_requests request
                JOIN latest_event latest USING (withdrawal_id)
                LEFT JOIN payout_ledger payout USING (withdrawal_id)
                WHERE request.created_at >= @from_at AND request.created_at < @to_at
                  AND (@courier_id IS NULL OR request.courier_id = @courier_id)
                  AND (@status IS NULL OR latest.action = @status)
            )
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
