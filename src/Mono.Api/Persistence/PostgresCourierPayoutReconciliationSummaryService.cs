using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutReconciliationSummaryBreakdown(string ReconciliationCode, long WithdrawalCount, long AmountToman);
public sealed record CourierPayoutReconciliationSummary(
    string OutcomeCode, long TotalWithdrawalCount, long DiscrepancyCount, long TotalAmountToman,
    IReadOnlyList<CourierPayoutReconciliationSummaryBreakdown> Breakdown);

public sealed class PostgresCourierPayoutReconciliationSummaryService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    private static readonly string[] Statuses = ["REQUESTED", "PROCESSING", "PAID", "REJECTED"];

    public async Task<CourierPayoutReconciliationSummary> ReadAsync(
        string? courierId, string? status, DateTimeOffset fromAt, DateTimeOffset toAt,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        if (fromAt >= toAt) throw new ArgumentOutOfRangeException(nameof(fromAt));
        if (courierId is not null && !IsCourierId(courierId))
            throw new ArgumentException("A valid courier ID is required.", nameof(courierId));
        if (status is not null && !Statuses.Contains(status, StringComparer.Ordinal))
            throw new ArgumentException("Unknown withdrawal status.", nameof(status));

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
                JOIN courier_withdrawal_requests request ON request.withdrawal_id = payout_tx.reference_id
                LEFT JOIN ledger_entries entry ON entry.ledger_transaction_id = payout_tx.ledger_transaction_id
                LEFT JOIN ledger_accounts account ON account.ledger_account_id = entry.ledger_account_id
                WHERE payout_tx.reference_type = 'WITHDRAWAL_PAYOUT'
                GROUP BY payout_tx.reference_id, request.courier_id, request.amount_toman
            ),
            reconciled AS (
                SELECT request.withdrawal_id, request.courier_id, request.amount_toman,
                       latest.action AS latest_action,
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
            ),
            grouped AS (
                SELECT reconciliation_code, COUNT(*)::bigint AS withdrawal_count,
                       COALESCE(SUM(amount_toman), 0)::bigint AS amount_toman
                FROM reconciled GROUP BY reconciliation_code
            )
            SELECT reconciliation_code, withdrawal_count, amount_toman
            FROM grouped ORDER BY reconciliation_code;
            """;
        var breakdown = new List<CourierPayoutReconciliationSummaryBreakdown>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
            command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                breakdown.Add(new CourierPayoutReconciliationSummaryBreakdown(
                    reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }
        var totalCount = breakdown.Sum(item => item.WithdrawalCount);
        var discrepancyCount = breakdown.Where(item => item.ReconciliationCode != "MATCHED").Sum(item => item.WithdrawalCount);
        var totalAmount = breakdown.Sum(item => item.AmountToman);
        var outcome = totalCount == 0 ? "EMPTY" : "FOUND";
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO courier_payout_reconciliation_summary_read_audit
                (read_audit_id, actor_id, courier_id_filter, status_filter, created_from_filter,
                 created_to_filter, total_withdrawal_count, discrepancy_count, total_amount_toman,
                 outcome_code, occurred_at)
            VALUES (@id, @actor, @courier, @status, @from_at, @to_at, @total_count,
                    @discrepancies, @amount, @outcome, @at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cps_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("actor", NpgsqlDbType.Text, operationsContext.ActorId);
            audit.Parameters.Add("courier", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
            audit.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            audit.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            audit.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            audit.Parameters.AddWithValue("total_count", NpgsqlDbType.Bigint, totalCount);
            audit.Parameters.AddWithValue("discrepancies", NpgsqlDbType.Bigint, discrepancyCount);
            audit.Parameters.AddWithValue("amount", NpgsqlDbType.Bigint, totalAmount);
            audit.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
            audit.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutReconciliationSummary(outcome, totalCount, discrepancyCount, totalAmount, breakdown);
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}