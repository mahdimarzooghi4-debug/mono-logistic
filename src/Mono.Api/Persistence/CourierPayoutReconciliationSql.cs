using System;

namespace Mono.Api.Persistence;

internal static class CourierPayoutReconciliationSql
{
    public const string CommonCtes = """
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
        """;
}