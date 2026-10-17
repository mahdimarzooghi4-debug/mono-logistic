-- Audit every administrator payout reconciliation report page.
CREATE TABLE IF NOT EXISTS courier_payout_reconciliation_read_audit (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cpr_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    courier_id_filter text NULL CHECK (courier_id_filter IS NULL OR courier_id_filter ~ '^crr_[0-9a-f]{32}$'),
    status_filter text NULL CHECK (status_filter IS NULL OR status_filter IN ('REQUESTED', 'PROCESSING', 'PAID', 'REJECTED')),
    created_from_filter timestamp with time zone NOT NULL,
    created_to_filter timestamp with time zone NOT NULL,
    only_discrepancies boolean NOT NULL,
    cursor_created_at timestamp with time zone NULL,
    cursor_withdrawal_id text NULL CHECK (cursor_withdrawal_id IS NULL OR cursor_withdrawal_id ~ '^wdr_[0-9a-f]{32}$'),
    page_size integer NOT NULL CHECK (page_size BETWEEN 1 AND 100),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'EMPTY')),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT courier_payout_reconciliation_read_audit_range_check CHECK (created_from_filter < created_to_filter),
    CONSTRAINT courier_payout_reconciliation_read_audit_cursor_check CHECK (
        (cursor_created_at IS NULL) = (cursor_withdrawal_id IS NULL))
);
CREATE INDEX IF NOT EXISTS courier_payout_reconciliation_read_audit_history_idx
    ON courier_payout_reconciliation_read_audit (actor_id, occurred_at DESC, read_audit_id DESC);
DROP TRIGGER IF EXISTS courier_payout_reconciliation_read_audit_immutable
    ON courier_payout_reconciliation_read_audit;
CREATE TRIGGER courier_payout_reconciliation_read_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_reconciliation_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_reconciliation_read_audit_no_truncate
    ON courier_payout_reconciliation_read_audit;
CREATE TRIGGER courier_payout_reconciliation_read_audit_no_truncate
    BEFORE TRUNCATE ON courier_payout_reconciliation_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
