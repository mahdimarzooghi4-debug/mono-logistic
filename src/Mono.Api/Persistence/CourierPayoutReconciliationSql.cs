namespace Mono.Api.Persistence;

internal static class CourierPayoutReconciliationSql
{
    public const string CommonCtes = """
        WITH scoped_requests AS (
            SELECT request.withdrawal_id, request.courier_id, request.amount_toman,
                   request.created_at, latest.action AS latest_action
            FROM courier_withdrawal_requests request
            JOIN LATERAL (
                SELECT event.action
                FROM courier_withdrawal_events event
                WHERE event.withdrawal_id = request.withdrawal_id
                ORDER BY event.withdrawal_event_sequence DESC
                LIMIT 1
            ) latest ON true
            WHERE request.created_at >= @from_at AND request.created_at < @to_at
              AND (@courier_id IS NULL OR request.courier_id = @courier_id)
              AND (@status IS NULL OR latest.action = @status)
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
            FROM scoped_requests request
            JOIN ledger_transactions payout_tx
              ON payout_tx.reference_id = request.withdrawal_id
             AND payout_tx.reference_type = 'WITHDRAWAL_PAYOUT'
            LEFT JOIN ledger_entries entry
              ON entry.ledger_transaction_id = payout_tx.ledger_transaction_id
            LEFT JOIN ledger_accounts account
              ON account.ledger_account_id = entry.ledger_account_id
            GROUP BY payout_tx.reference_id, request.courier_id, request.amount_toman
        ),
        reconciled AS (
            SELECT request.withdrawal_id, request.courier_id, request.amount_toman,
                   request.latest_action, request.created_at,
                   COALESCE(payout.transaction_count, 0) AS transaction_count,
                   COALESCE(payout.entry_count, 0) AS entry_count,
                   COALESCE(payout.courier_debit_count, 0) AS courier_debit_count,
                   COALESCE(payout.clearing_credit_count, 0) AS clearing_credit_count,
                   CASE
                     WHEN COALESCE(payout.transaction_count, 0) > 1 THEN 'MULTIPLE_PAYOUTS'
                     WHEN request.latest_action = 'PAID' AND COALESCE(payout.transaction_count, 0) = 0 THEN 'MISSING_PAYOUT'
                     WHEN request.latest_action <> 'PAID' AND COALESCE(payout.transaction_count, 0) > 0 THEN 'UNEXPECTED_PAYOUT'
                     WHEN request.latest_action <> 'PAID' AND COALESCE(payout.transaction_count, 0) = 0 THEN 'MATCHED'
                     WHEN payout.transaction_count = 1 AND payout.entry_count = 2
                      AND payout.courier_debit_count = 1 AND payout.clearing_credit_count = 1 THEN 'MATCHED'
                     ELSE 'LEDGER_MISMATCH'
                   END AS reconciliation_code
            FROM scoped_requests request
            LEFT JOIN payout_ledger payout USING (withdrawal_id)
        )
        """;
}