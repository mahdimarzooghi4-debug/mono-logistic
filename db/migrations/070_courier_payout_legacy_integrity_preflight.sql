-- Refuse deployment when existing payouts do not meet the current ledger invariant.
CREATE OR REPLACE FUNCTION assert_existing_courier_payout_ledger_integrity()
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    invalid_transaction_id text;
BEGIN
    WITH payout_entry_summary AS (
        SELECT entry.ledger_transaction_id,
               COUNT(*) AS entry_count,
               COUNT(*) FILTER (
                   WHERE account.owner_type = 'COURIER'
                     AND account.owner_id = request.courier_id
                     AND account.account_code = 'COURIER_AVAILABLE'
                     AND account.currency = ledger_tx.currency
                     AND entry.debit_amount = request.amount_toman
                     AND entry.credit_amount = 0) AS courier_debit_count,
               COUNT(*) FILTER (
                   WHERE account.owner_type = 'MONO'
                     AND account.owner_id = 'mono'
                     AND account.account_code = 'MONO_PAYOUT_CLEARING'
                     AND account.currency = ledger_tx.currency
                     AND entry.debit_amount = 0
                     AND entry.credit_amount = request.amount_toman) AS clearing_credit_count
        FROM ledger_transactions ledger_tx
        LEFT JOIN courier_withdrawal_requests request
            ON request.withdrawal_id = ledger_tx.reference_id
        JOIN ledger_entries entry
            ON entry.ledger_transaction_id = ledger_tx.ledger_transaction_id
        JOIN ledger_accounts account
            ON account.ledger_account_id = entry.ledger_account_id
        WHERE ledger_tx.reference_type = 'WITHDRAWAL_PAYOUT'
        GROUP BY entry.ledger_transaction_id, request.courier_id, request.amount_toman, ledger_tx.currency
    ),
    latest_event AS (
        SELECT DISTINCT ON (event.withdrawal_id)
               event.withdrawal_id, event.action, event.actor_type, event.bank_reference
        FROM courier_withdrawal_events event
        ORDER BY event.withdrawal_id, event.withdrawal_event_sequence DESC
    )
    SELECT ledger_tx.ledger_transaction_id
      INTO invalid_transaction_id
    FROM ledger_transactions ledger_tx
    LEFT JOIN courier_withdrawal_requests request
        ON request.withdrawal_id = ledger_tx.reference_id
    LEFT JOIN latest_event event
        ON event.withdrawal_id = ledger_tx.reference_id
    LEFT JOIN payout_entry_summary summary
        ON summary.ledger_transaction_id = ledger_tx.ledger_transaction_id
    WHERE ledger_tx.reference_type = 'WITHDRAWAL_PAYOUT'
      AND (
          request.withdrawal_id IS NULL
          OR request.amount_toman <= 0
          OR event.action IS DISTINCT FROM 'PAID'
          OR event.actor_type IS DISTINCT FROM 'OPERATIONS'
          OR event.bank_reference IS NULL
          OR summary.entry_count IS DISTINCT FROM 2
          OR summary.courier_debit_count IS DISTINCT FROM 1
          OR summary.clearing_credit_count IS DISTINCT FROM 1
      )
    ORDER BY ledger_tx.ledger_transaction_id
    LIMIT 1;

    IF invalid_transaction_id IS NOT NULL THEN
        RAISE EXCEPTION
            'pre-existing payout ledger transaction violates courier payout integrity: %',
            invalid_transaction_id;
    END IF;
END;
$$;

SELECT assert_existing_courier_payout_ledger_integrity();
