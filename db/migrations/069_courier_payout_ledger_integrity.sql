-- Enforce that manual withdrawal payouts exactly settle the requested amount.
CREATE OR REPLACE FUNCTION validate_courier_payout_ledger_integrity()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    payout_withdrawal_id text;
    payout_courier_id text;
    payout_amount bigint;
    latest_action text;
    latest_actor_type text;
    latest_bank_reference text;
    entry_count bigint;
    courier_debit_count bigint;
    clearing_credit_count bigint;
BEGIN
    IF NEW.reference_type <> 'WITHDRAWAL_PAYOUT' THEN
        RETURN NULL;
    END IF;

    SELECT request.withdrawal_id, request.courier_id, request.amount_toman
      INTO payout_withdrawal_id, payout_courier_id, payout_amount
    FROM courier_withdrawal_requests request
    WHERE request.withdrawal_id = NEW.reference_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'payout ledger transaction references an unknown withdrawal';
    END IF;

    SELECT event.action, event.actor_type, event.bank_reference
      INTO latest_action, latest_actor_type, latest_bank_reference
    FROM courier_withdrawal_events event
    WHERE event.withdrawal_id = payout_withdrawal_id
    ORDER BY event.withdrawal_event_sequence DESC
    LIMIT 1;
    IF latest_action IS DISTINCT FROM 'PAID'
       OR latest_actor_type IS DISTINCT FROM 'OPERATIONS'
       OR latest_bank_reference IS NULL THEN
        RAISE EXCEPTION 'payout ledger transaction requires a completed operator payment';
    END IF;

    SELECT COUNT(*),
           COUNT(*) FILTER (
               WHERE account.owner_type = 'COURIER'
                 AND account.owner_id = payout_courier_id
                 AND account.account_code = 'COURIER_AVAILABLE'
                 AND account.currency = NEW.currency
                 AND entry.debit_amount = payout_amount
                 AND entry.credit_amount = 0),
           COUNT(*) FILTER (
               WHERE account.owner_type = 'MONO'
                 AND account.owner_id = 'mono'
                 AND account.account_code = 'MONO_PAYOUT_CLEARING'
                 AND account.currency = NEW.currency
                 AND entry.debit_amount = 0
                 AND entry.credit_amount = payout_amount)
      INTO entry_count, courier_debit_count, clearing_credit_count
    FROM ledger_entries entry
    JOIN ledger_accounts account ON account.ledger_account_id = entry.ledger_account_id
    WHERE entry.ledger_transaction_id = NEW.ledger_transaction_id;

    IF entry_count <> 2 OR courier_debit_count <> 1 OR clearing_credit_count <> 1 THEN
        RAISE EXCEPTION 'payout ledger transaction must debit the courier and credit clearing for the withdrawal amount';
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS courier_payout_ledger_integrity_check ON ledger_transactions;
CREATE CONSTRAINT TRIGGER courier_payout_ledger_integrity_check
    AFTER INSERT ON ledger_transactions
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION validate_courier_payout_ledger_integrity();
