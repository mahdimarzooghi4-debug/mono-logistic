-- Keep an append-only record of global payout-destination audit searches.
CREATE TABLE IF NOT EXISTS courier_payout_destination_operations_search_audit (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cps_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    courier_id_filter text NULL CHECK (courier_id_filter IS NULL OR courier_id_filter ~ '^crr_[0-9a-f]{32}$'),
    event_type_filter text NULL CHECK (event_type_filter IS NULL OR event_type_filter IN ('DESTINATION', 'SECRET_ACCESS')),
    action_filter text NULL CHECK (action_filter IS NULL OR action_filter IN ('VERIFIED', 'REVOKED', 'SECRET_READ')),
    actor_id_filter text NULL CHECK (actor_id_filter IS NULL OR length(btrim(actor_id_filter)) BETWEEN 1 AND 128),
    occurred_from_filter timestamp with time zone NULL,
    occurred_to_filter timestamp with time zone NULL,
    cursor_occurred_at timestamp with time zone NULL,
    cursor_event_key text NULL,
    page_size integer NOT NULL CHECK (page_size BETWEEN 1 AND 100),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'EMPTY')),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT courier_payout_destination_operations_search_audit_range_check CHECK (
        (occurred_from_filter IS NULL) = (occurred_to_filter IS NULL) AND
        (occurred_from_filter IS NULL OR occurred_from_filter < occurred_to_filter)),
    CONSTRAINT courier_payout_destination_operations_search_audit_cursor_check CHECK (
        (cursor_occurred_at IS NULL) = (cursor_event_key IS NULL))
);
CREATE INDEX IF NOT EXISTS courier_payout_destination_operations_search_audit_history_idx
    ON courier_payout_destination_operations_search_audit (actor_id, occurred_at DESC, read_audit_id DESC);
DROP TRIGGER IF EXISTS courier_payout_destination_operations_search_audit_immutable
    ON courier_payout_destination_operations_search_audit;
CREATE TRIGGER courier_payout_destination_operations_search_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_destination_operations_search_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_destination_operations_search_audit_no_truncate
    ON courier_payout_destination_operations_search_audit;
CREATE TRIGGER courier_payout_destination_operations_search_audit_no_truncate
    BEFORE TRUNCATE ON courier_payout_destination_operations_search_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
