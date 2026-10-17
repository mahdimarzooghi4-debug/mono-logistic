-- Record and protect every privileged read of courier-withdrawal search history.
CREATE TABLE IF NOT EXISTS courier_withdrawal_operations_audit_read (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cwa_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    actor_filter text NULL CHECK (actor_filter IS NULL OR length(btrim(actor_filter)) BETWEEN 1 AND 128),
    status_filter text NULL CHECK (
        status_filter IS NULL OR status_filter IN ('REQUESTED', 'PROCESSING', 'PAID', 'REJECTED')),
    courier_id_filter text NULL CHECK (
        courier_id_filter IS NULL OR courier_id_filter ~ '^crr_[0-9a-f]{32}$'),
    occurred_from_filter timestamp with time zone NULL,
    occurred_to_filter timestamp with time zone NULL,
    cursor_occurred_at timestamp with time zone NULL,
    cursor_search_audit_id text NULL CHECK (
        cursor_search_audit_id IS NULL OR cursor_search_audit_id ~ '^cws_[0-9a-f]{32}$'),
    page_size integer NOT NULL CHECK (page_size BETWEEN 1 AND 100),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'EMPTY')),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT courier_withdrawal_operations_audit_read_time_range_check CHECK (
        (occurred_from_filter IS NULL) = (occurred_to_filter IS NULL) AND
        (occurred_from_filter IS NULL OR occurred_from_filter < occurred_to_filter)),
    CONSTRAINT courier_withdrawal_operations_audit_read_cursor_check CHECK (
        (cursor_occurred_at IS NULL) = (cursor_search_audit_id IS NULL))
);

CREATE INDEX IF NOT EXISTS courier_withdrawal_operations_audit_read_history_idx
    ON courier_withdrawal_operations_audit_read (actor_id, occurred_at DESC, read_audit_id DESC);

DROP TRIGGER IF EXISTS courier_withdrawal_operations_audit_read_immutable
    ON courier_withdrawal_operations_audit_read;
CREATE TRIGGER courier_withdrawal_operations_audit_read_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_operations_audit_read
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_withdrawal_operations_audit_read_no_truncate
    ON courier_withdrawal_operations_audit_read;
CREATE TRIGGER courier_withdrawal_operations_audit_read_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_operations_audit_read
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
