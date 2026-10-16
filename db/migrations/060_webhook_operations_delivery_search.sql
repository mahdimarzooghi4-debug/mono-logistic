-- Expand privileged webhook reads with auditable queue searches and delivery detail access.
ALTER TABLE webhook_operations_read_audit
    ADD COLUMN IF NOT EXISTS resource_id text;

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_action_check;

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_resource_id_check;

ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_action_check
        CHECK (action IN ('QUEUE_SUMMARY', 'QUEUE_SEARCH', 'DELIVERY_DETAIL'));

ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_resource_id_check
        CHECK ((action = 'DELIVERY_DETAIL' AND resource_id ~ '^whd_[0-9a-f]{32}$') OR
               (action <> 'DELIVERY_DETAIL' AND resource_id IS NULL));
