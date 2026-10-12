-- Append-only access audit for cross-customer Delivery Request operations reads.
CREATE TABLE IF NOT EXISTS delivery_request_operations_read_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^dra_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(trim(actor_id)) > 0),
    action text NOT NULL CHECK (action IN ('DELIVERY_REQUEST_SEARCH', 'DELIVERY_REQUEST_DETAIL')),
    delivery_request_id text NULL CHECK (
        delivery_request_id IS NULL OR delivery_request_id ~ '^drq_[0-9a-f]{32}$'),
    result_count integer NOT NULL CHECK (result_count >= 0),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS delivery_request_operations_read_audit_history_idx
    ON delivery_request_operations_read_audit (occurred_at DESC, audit_id DESC);
CREATE INDEX IF NOT EXISTS delivery_request_operations_read_audit_request_idx
    ON delivery_request_operations_read_audit (delivery_request_id, occurred_at DESC, audit_id DESC)
    WHERE delivery_request_id IS NOT NULL;

CREATE OR REPLACE FUNCTION reject_delivery_request_operations_read_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Delivery Request operations read audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS delivery_request_operations_read_audit_no_update_delete
    ON delivery_request_operations_read_audit;
CREATE TRIGGER delivery_request_operations_read_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON delivery_request_operations_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_delivery_request_operations_read_audit_mutation();

DROP TRIGGER IF EXISTS delivery_request_operations_read_audit_no_truncate
    ON delivery_request_operations_read_audit;
CREATE TRIGGER delivery_request_operations_read_audit_no_truncate
    BEFORE TRUNCATE ON delivery_request_operations_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_delivery_request_operations_read_audit_mutation();

-- Support stable cross-customer search and keyset paging.
CREATE INDEX IF NOT EXISTS delivery_requests_operations_page_idx
    ON delivery_requests (created_at DESC, delivery_request_id DESC);
CREATE INDEX IF NOT EXISTS delivery_requests_external_reference_prefix_idx
    ON delivery_requests (lower(external_reference) text_pattern_ops, delivery_request_id);
