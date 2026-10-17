-- Audit administrator reads of courier withdrawal lifecycle events.
CREATE TABLE IF NOT EXISTS courier_withdrawal_operations_event_search_audit (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cwe_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    action_filter text NULL CHECK (action_filter IS NULL OR action_filter IN ('REQUESTED', 'PROCESSING', 'PAID', 'REJECTED')),
    actor_type_filter text NULL CHECK (actor_type_filter IS NULL OR actor_type_filter IN ('COURIER', 'OPERATIONS')),
    actor_id_filter text NULL CHECK (actor_id_filter IS NULL OR length(btrim(actor_id_filter)) BETWEEN 1 AND 128),
    courier_id_filter text NULL CHECK (courier_id_filter IS NULL OR courier_id_filter ~ '^crr_[0-9a-f]{32}$'),
    occurred_from_filter timestamp with time zone NULL,
    occurred_to_filter timestamp with time zone NULL,
    cursor_sequence bigint NULL CHECK (cursor_sequence IS NULL OR cursor_sequence > 0),
    page_size integer NOT NULL CHECK (page_size BETWEEN 1 AND 100),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'EMPTY')),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT courier_withdrawal_operations_event_search_audit_time_range_check CHECK (
        (occurred_from_filter IS NULL) = (occurred_to_filter IS NULL) AND
        (occurred_from_filter IS NULL OR occurred_from_filter < occurred_to_filter))
);
CREATE INDEX IF NOT EXISTS courier_withdrawal_operations_event_search_audit_history_idx
    ON courier_withdrawal_operations_event_search_audit (actor_id, occurred_at DESC, read_audit_id DESC);
DROP TRIGGER IF EXISTS courier_withdrawal_operations_event_search_audit_immutable
    ON courier_withdrawal_operations_event_search_audit;
CREATE TRIGGER courier_withdrawal_operations_event_search_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_operations_event_search_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_withdrawal_operations_event_search_audit_no_truncate
    ON courier_withdrawal_operations_event_search_audit;
CREATE TRIGGER courier_withdrawal_operations_event_search_audit_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_operations_event_search_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
