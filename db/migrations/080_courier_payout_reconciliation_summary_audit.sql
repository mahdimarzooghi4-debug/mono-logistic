-- Audit administrator payout reconciliation aggregate reads.
CREATE TABLE IF NOT EXISTS courier_payout_reconciliation_summary_read_audit (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cps_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    courier_id_filter text NULL CHECK (courier_id_filter IS NULL OR courier_id_filter ~ '^crr_[0-9a-f]{32}$'),
    status_filter text NULL CHECK (status_filter IS NULL OR status_filter IN ('REQUESTED', 'PROCESSING', 'PAID', 'REJECTED')),
    created_from_filter timestamp with time zone NOT NULL,
    created_to_filter timestamp with time zone NOT NULL,
    total_withdrawal_count bigint NOT NULL CHECK (total_withdrawal_count >= 0),
    discrepancy_count bigint NOT NULL CHECK (discrepancy_count BETWEEN 0 AND total_withdrawal_count),
    total_amount_toman bigint NOT NULL CHECK (total_amount_toman >= 0),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'EMPTY')),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT courier_payout_reconciliation_summary_read_audit_range_check CHECK (created_from_filter < created_to_filter),
    CONSTRAINT courier_payout_reconciliation_summary_read_audit_outcome_check CHECK (
        (outcome_code = 'FOUND' AND total_withdrawal_count > 0)
        OR (outcome_code = 'EMPTY' AND total_withdrawal_count = 0))
);
CREATE INDEX IF NOT EXISTS courier_payout_reconciliation_summary_read_audit_history_idx
    ON courier_payout_reconciliation_summary_read_audit (actor_id, occurred_at DESC, read_audit_id DESC);
DROP TRIGGER IF EXISTS courier_payout_reconciliation_summary_read_audit_immutable ON courier_payout_reconciliation_summary_read_audit;
CREATE TRIGGER courier_payout_reconciliation_summary_read_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_reconciliation_summary_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_reconciliation_summary_read_audit_no_truncate ON courier_payout_reconciliation_summary_read_audit;
CREATE TRIGGER courier_payout_reconciliation_summary_read_audit_no_truncate
    BEFORE TRUNCATE ON courier_payout_reconciliation_summary_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
