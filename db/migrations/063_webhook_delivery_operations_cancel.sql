-- Add idempotent, audited cancellation for queued webhook deliveries.
CREATE TABLE IF NOT EXISTS webhook_delivery_cancellation_requests (
    delivery_id text NOT NULL CHECK (delivery_id ~ '^whd_[0-9a-f]{32}$'),
    idempotency_key text NOT NULL CHECK (length(idempotency_key) BETWEEN 1 AND 128),
    outcome_code text NOT NULL CHECK (outcome_code IN (
        'CANCELLED', 'ALREADY_CANCELLED', 'NOT_FOUND', 'NOT_CANCELLABLE')),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (delivery_id, idempotency_key)
);

CREATE OR REPLACE FUNCTION reject_webhook_delivery_cancellation_request_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'webhook delivery cancellation requests are append-only';
END;
$$;

DROP TRIGGER IF EXISTS webhook_delivery_cancellation_requests_immutable
    ON webhook_delivery_cancellation_requests;
CREATE TRIGGER webhook_delivery_cancellation_requests_immutable
    BEFORE UPDATE OR DELETE ON webhook_delivery_cancellation_requests
    FOR EACH ROW EXECUTE FUNCTION reject_webhook_delivery_cancellation_request_mutation();

DROP TRIGGER IF EXISTS webhook_delivery_cancellation_requests_no_truncate
    ON webhook_delivery_cancellation_requests;
CREATE TRIGGER webhook_delivery_cancellation_requests_no_truncate
    BEFORE TRUNCATE ON webhook_delivery_cancellation_requests
    FOR EACH STATEMENT EXECUTE FUNCTION reject_webhook_delivery_cancellation_request_mutation();

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_action_check;
ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_action_check
        CHECK (action IN (
            'QUEUE_SUMMARY', 'QUEUE_SEARCH', 'DELIVERY_DETAIL', 'DELIVERY_REPLAY',
            'DELIVERY_CANCEL', 'AUDIT_HISTORY_READ'));

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_resource_id_check;
ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_resource_id_check
        CHECK ((action IN ('DELIVERY_DETAIL', 'DELIVERY_REPLAY', 'DELIVERY_CANCEL') AND
                resource_id IS NOT NULL AND resource_id ~ '^whd_[0-9a-f]{32}$') OR
               (action NOT IN ('DELIVERY_DETAIL', 'DELIVERY_REPLAY', 'DELIVERY_CANCEL') AND
                resource_id IS NULL));

ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_outcome_code_check;
ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_outcome_code_check
        CHECK ((action = 'DELIVERY_REPLAY' AND outcome_code IS NOT NULL AND outcome_code IN (
                    'QUEUED', 'ALREADY_REQUESTED', 'NOT_FOUND', 'NOT_REPLAYABLE',
                    'ENDPOINT_DISABLED', 'REPLAY_LIMIT_REACHED')) OR
               (action = 'DELIVERY_CANCEL' AND outcome_code IS NOT NULL AND outcome_code IN (
                    'CANCELLED', 'ALREADY_CANCELLED', 'ALREADY_REQUESTED', 'NOT_FOUND',
                    'NOT_CANCELLABLE')) OR
               (action NOT IN ('DELIVERY_REPLAY', 'DELIVERY_CANCEL') AND outcome_code IS NULL));
