-- Per-endpoint delivery queue derived from immutable transactional outbox events.
CREATE TABLE IF NOT EXISTS webhook_deliveries (
    delivery_id text PRIMARY KEY CHECK (delivery_id ~ '^whd_[0-9a-f]{32}$'),
    endpoint_id text NOT NULL,
    customer_id text NOT NULL REFERENCES customers(customer_id),
    event_id text NOT NULL REFERENCES outbox_events(event_id),
    event_type text NOT NULL,
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
    status text NOT NULL DEFAULT 'PENDING'
        CHECK (status IN ('PENDING', 'IN_PROGRESS', 'RETRYING', 'SUCCEEDED', 'DEAD_LETTERED', 'CANCELLED')),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count BETWEEN 0 AND 5),
    next_attempt_at timestamp with time zone NOT NULL,
    lease_expires_at timestamp with time zone NULL,
    last_attempt_at timestamp with time zone NULL,
    last_http_status integer NULL CHECK (last_http_status BETWEEN 100 AND 599),
    last_error_code text NULL CHECK (last_error_code IS NULL OR length(last_error_code) <= 64),
    created_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone NULL,
    CONSTRAINT webhook_deliveries_endpoint_fk
        FOREIGN KEY (customer_id, endpoint_id)
        REFERENCES webhook_endpoints(customer_id, endpoint_id),
    CONSTRAINT webhook_deliveries_endpoint_event_unique UNIQUE (endpoint_id, event_id),
    CONSTRAINT webhook_deliveries_lease_consistent
        CHECK ((status = 'IN_PROGRESS' AND lease_expires_at IS NOT NULL) OR
               (status <> 'IN_PROGRESS' AND lease_expires_at IS NULL)),
    CONSTRAINT webhook_deliveries_completion_consistent
        CHECK ((status IN ('SUCCEEDED', 'DEAD_LETTERED', 'CANCELLED') AND completed_at IS NOT NULL) OR
               (status IN ('PENDING', 'IN_PROGRESS', 'RETRYING') AND completed_at IS NULL))
);

CREATE INDEX IF NOT EXISTS webhook_deliveries_due_idx
    ON webhook_deliveries (next_attempt_at, created_at, delivery_id)
    WHERE status IN ('PENDING', 'RETRYING');

CREATE INDEX IF NOT EXISTS webhook_deliveries_expired_lease_idx
    ON webhook_deliveries (lease_expires_at, delivery_id)
    WHERE status = 'IN_PROGRESS';

CREATE TABLE IF NOT EXISTS webhook_delivery_attempt_events (
    attempt_event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    delivery_id text NOT NULL REFERENCES webhook_deliveries(delivery_id),
    attempt_number integer NOT NULL CHECK (attempt_number BETWEEN 1 AND 5),
    action text NOT NULL CHECK (action IN (
        'STARTED', 'SUCCEEDED', 'RETRY_SCHEDULED', 'DEAD_LETTERED', 'LEASE_EXPIRED')),
    http_status integer NULL CHECK (http_status BETWEEN 100 AND 599),
    error_code text NULL CHECK (error_code IS NULL OR length(error_code) <= 64),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS webhook_delivery_attempt_events_history_idx
    ON webhook_delivery_attempt_events (delivery_id, attempt_number, attempt_event_id);

CREATE OR REPLACE FUNCTION reject_webhook_delivery_attempt_event_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'webhook delivery attempt events are append-only';
END;
$$;

DROP TRIGGER IF EXISTS webhook_delivery_attempt_events_immutable ON webhook_delivery_attempt_events;
CREATE TRIGGER webhook_delivery_attempt_events_immutable
    BEFORE UPDATE OR DELETE ON webhook_delivery_attempt_events
    FOR EACH ROW EXECUTE FUNCTION reject_webhook_delivery_attempt_event_mutation();

DROP TRIGGER IF EXISTS webhook_delivery_attempt_events_no_truncate ON webhook_delivery_attempt_events;
CREATE TRIGGER webhook_delivery_attempt_events_no_truncate
    BEFORE TRUNCATE ON webhook_delivery_attempt_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_webhook_delivery_attempt_event_mutation();
