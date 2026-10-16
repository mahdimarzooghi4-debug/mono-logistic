-- Manual replay creates a new bounded delivery generation and preserves prior attempts.
ALTER TABLE webhook_deliveries
    ADD COLUMN IF NOT EXISTS replay_generation integer NOT NULL DEFAULT 0
        CHECK (replay_generation BETWEEN 0 AND 3);

ALTER TABLE webhook_deliveries
    DROP CONSTRAINT IF EXISTS webhook_deliveries_endpoint_event_unique;

CREATE UNIQUE INDEX IF NOT EXISTS webhook_deliveries_endpoint_event_generation_unique
    ON webhook_deliveries (endpoint_id, event_id, replay_generation);

CREATE TABLE IF NOT EXISTS webhook_delivery_replay_requests (
    original_delivery_id text NOT NULL REFERENCES webhook_deliveries(delivery_id),
    idempotency_key text NOT NULL CHECK (length(idempotency_key) BETWEEN 1 AND 128),
    replay_delivery_id text NOT NULL UNIQUE REFERENCES webhook_deliveries(delivery_id),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (original_delivery_id, idempotency_key),
    CHECK (original_delivery_id <> replay_delivery_id)
);

CREATE OR REPLACE FUNCTION reject_webhook_delivery_replay_request_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'webhook delivery replay requests are append-only';
END;
$$;

DROP TRIGGER IF EXISTS webhook_delivery_replay_requests_immutable ON webhook_delivery_replay_requests;
CREATE TRIGGER webhook_delivery_replay_requests_immutable
    BEFORE UPDATE OR DELETE ON webhook_delivery_replay_requests
    FOR EACH ROW EXECUTE FUNCTION reject_webhook_delivery_replay_request_mutation();

DROP TRIGGER IF EXISTS webhook_delivery_replay_requests_no_truncate ON webhook_delivery_replay_requests;
CREATE TRIGGER webhook_delivery_replay_requests_no_truncate
    BEFORE TRUNCATE ON webhook_delivery_replay_requests
    FOR EACH STATEMENT EXECUTE FUNCTION reject_webhook_delivery_replay_request_mutation();
