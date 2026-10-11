-- Extend privileged Delivery Request read audit with self-audited inspection access.
ALTER TABLE delivery_request_operations_read_audit
    DROP CONSTRAINT IF EXISTS delivery_request_operations_read_audit_action_check;

ALTER TABLE delivery_request_operations_read_audit
    ADD CONSTRAINT delivery_request_operations_read_audit_action_check
    CHECK (action IN (
        'DELIVERY_REQUEST_SEARCH',
        'DELIVERY_REQUEST_DETAIL',
        'DELIVERY_REQUEST_AUDIT_INSPECTION'
    ));

CREATE INDEX IF NOT EXISTS delivery_request_operations_read_audit_actor_history_idx
    ON delivery_request_operations_read_audit (actor_id, occurred_at DESC, audit_id DESC);
