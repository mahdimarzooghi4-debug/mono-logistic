-- Expand privileged webhook reads with auditable queue searches and delivery detail access.
CREATE INDEX IF NOT EXISTS webhook_deliveries_operations_history_idx
    ON webhook_deliveries (created_at DESC, delivery_id DESC);

ALTER TABLE webhook_operations_read_audit
    ADD COLUMN IF NOT EXISTS resource_id text;

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_action_check;

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_resource_id_check;

ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_action_check
        CHECK (action IN ('QUEUE_SUMMARY', 'QUEUE_SEARCH', 'DELIVERY_DETAIL', 'DELIVERY_REPLAY'));

ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_resource_id_check
        CHECK ((action IN ('DELIVERY_DETAIL', 'DELIVERY_REPLAY') AND resource_id IS NOT NULL AND
                resource_id ~ '^whd_[0-9a-f]{32}$') OR
               (action NOT IN ('DELIVERY_DETAIL', 'DELIVERY_REPLAY') AND resource_id IS NULL));
