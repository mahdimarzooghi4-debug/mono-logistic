-- Append-only lifecycle history for customer webhook endpoint management.
CREATE TABLE IF NOT EXISTS webhook_endpoint_events (
    event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    endpoint_id text NOT NULL,
    customer_id text NOT NULL REFERENCES customers(customer_id),
    action text NOT NULL CHECK (action IN ('CREATED', 'SECRET_ROTATED', 'DISABLED')),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT webhook_endpoint_events_endpoint_fk
        FOREIGN KEY (customer_id, endpoint_id)
        REFERENCES webhook_endpoints(customer_id, endpoint_id)
);

CREATE INDEX IF NOT EXISTS webhook_endpoint_events_history_idx
    ON webhook_endpoint_events (customer_id, endpoint_id, occurred_at DESC, event_id DESC);

CREATE INDEX IF NOT EXISTS webhook_endpoints_customer_page_idx
    ON webhook_endpoints (customer_id, created_at DESC, endpoint_id DESC);

CREATE OR REPLACE FUNCTION reject_webhook_endpoint_event_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'webhook endpoint lifecycle events are append-only';
END;
$$;

DROP TRIGGER IF EXISTS webhook_endpoint_events_immutable ON webhook_endpoint_events;
CREATE TRIGGER webhook_endpoint_events_immutable
    BEFORE UPDATE OR DELETE ON webhook_endpoint_events
    FOR EACH ROW EXECUTE FUNCTION reject_webhook_endpoint_event_mutation();

DROP TRIGGER IF EXISTS webhook_endpoint_events_no_truncate ON webhook_endpoint_events;
CREATE TRIGGER webhook_endpoint_events_no_truncate
    BEFORE TRUNCATE ON webhook_endpoint_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_webhook_endpoint_event_mutation();
