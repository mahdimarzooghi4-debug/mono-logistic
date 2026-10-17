-- Audit each administrator payout ledger drilldown, including not-found probes.
CREATE TABLE IF NOT EXISTS courier_payout_ledger_detail_read_audit (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cpl_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    withdrawal_id text NOT NULL CHECK (withdrawal_id ~ '^wdr_[0-9a-f]{32}$'),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'PAYOUT_NOT_POSTED', 'WITHDRAWAL_NOT_FOUND')),
    transaction_count integer NOT NULL CHECK (transaction_count BETWEEN 0 AND 1000),
    entry_count integer NOT NULL CHECK (entry_count BETWEEN 0 AND 10000),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT courier_payout_ledger_detail_read_audit_counts_check CHECK (
        (outcome_code = 'FOUND' AND transaction_count > 0)
        OR (outcome_code <> 'FOUND' AND transaction_count = 0 AND entry_count = 0))
);
CREATE INDEX IF NOT EXISTS courier_payout_ledger_detail_read_audit_history_idx
    ON courier_payout_ledger_detail_read_audit (withdrawal_id, occurred_at DESC, read_audit_id DESC);
DROP TRIGGER IF EXISTS courier_payout_ledger_detail_read_audit_immutable ON courier_payout_ledger_detail_read_audit;
CREATE TRIGGER courier_payout_ledger_detail_read_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_ledger_detail_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_ledger_detail_read_audit_no_truncate ON courier_payout_ledger_detail_read_audit;
CREATE TRIGGER courier_payout_ledger_detail_read_audit_no_truncate
    BEFORE TRUNCATE ON courier_payout_ledger_detail_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
