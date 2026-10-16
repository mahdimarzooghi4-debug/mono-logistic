-- Allow auditable privileged replay requests while binding outcomes to each delivery.
ALTER TABLE webhook_operations_read_audit
    ADD COLUMN IF NOT EXISTS outcome_code text;

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_action_check;

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_resource_id_check;

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_outcome_code_check;

ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_action_check
        CHECK (action IN ('QUEUE_SUMMARY', 'QUEUE_SEARCH', 'DELIVERY_DETAIL', 'DELIVERY_REPLAY'));

ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_resource_id_check
        CHECK ((action IN ('DELIVERY_DETAIL', 'DELIVERY_REPLAY') AND resource_id IS NOT NULL AND
                resource_id ~ '^whd_[0-9a-f]{32}$') OR
               (action NOT IN ('DELIVERY_DETAIL', 'DELIVERY_REPLAY') AND resource_id IS NULL));

ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_outcome_code_check
        CHECK ((action = 'DELIVERY_REPLAY' AND outcome_code IS NOT NULL AND outcome_code IN (
                    'QUEUED', 'ALREADY_REQUESTED', 'NOT_FOUND', 'NOT_REPLAYABLE',
                    'ENDPOINT_DISABLED', 'REPLAY_LIMIT_REACHED')) OR
               (action <> 'DELIVERY_REPLAY' AND outcome_code IS NULL));
