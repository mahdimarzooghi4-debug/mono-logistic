-- Record privileged courier-withdrawal queue searches for finance-operations traceability.
CREATE TABLE IF NOT EXISTS courier_withdrawal_operations_search_audit (
    search_audit_id text PRIMARY KEY CHECK (search_audit_id ~ '^cws_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    status_filter text NULL CHECK (
        status_filter IS NULL OR status_filter IN ('REQUESTED', 'PROCESSING', 'PAID', 'REJECTED')),
    courier_id_filter text NULL CHECK (
        courier_id_filter IS NULL OR courier_id_filter ~ '^crr_[0-9a-f]{32}$'),
    cursor_sequence bigint NULL CHECK (cursor_sequence IS NULL OR cursor_sequence > 0),
    page_size integer NOT NULL CHECK (page_size BETWEEN 1 AND 100),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS courier_withdrawal_operations_search_audit_history_idx
    ON courier_withdrawal_operations_search_audit (actor_id, occurred_at DESC, search_audit_id DESC);

DROP TRIGGER IF EXISTS courier_withdrawal_operations_search_audit_immutable
    ON courier_withdrawal_operations_search_audit;
CREATE TRIGGER courier_withdrawal_operations_search_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_operations_search_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_withdrawal_operations_search_audit_no_truncate
    ON courier_withdrawal_operations_search_audit;
CREATE TRIGGER courier_withdrawal_operations_search_audit_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_operations_search_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
