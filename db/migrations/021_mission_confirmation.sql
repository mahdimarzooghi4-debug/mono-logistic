-- A customer can explicitly confirm one request once; Mission creation and its event commit together.
CREATE UNIQUE INDEX IF NOT EXISTS delivery_quote_versions_request_quote_version_idx
    ON delivery_quote_versions (customer_id, delivery_request_id, quote_id, version);
CREATE UNIQUE INDEX IF NOT EXISTS promise_versions_promise_version_customer_idx
    ON promise_versions (promise_id, version, customer_id);
ALTER TABLE outbox_events
    DROP CONSTRAINT IF EXISTS outbox_events_event_type_check;
ALTER TABLE outbox_events
    ADD CONSTRAINT outbox_events_event_type_check
    CHECK (event_type IN ('promise.committed', 'promise.revised', 'mission.created'));

CREATE TABLE IF NOT EXISTS missions (
    mission_id text PRIMARY KEY CHECK (mission_id ~ '^mis_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    delivery_request_id text NOT NULL,
    quote_id text NOT NULL,
    quote_version integer NOT NULL CHECK (quote_version > 0),
    promise_id text NOT NULL,
    promise_version integer NOT NULL CHECK (promise_version > 0),
    status text NOT NULL CHECK (status = 'PROMISED'),
    committed_delivery_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT missions_customer_request_unique UNIQUE (customer_id, delivery_request_id),
    CONSTRAINT missions_customer_request_mission_unique UNIQUE (customer_id, delivery_request_id, mission_id),
    CONSTRAINT missions_request_fk
        FOREIGN KEY (customer_id, delivery_request_id)
        REFERENCES delivery_requests (customer_id, delivery_request_id),
    CONSTRAINT missions_quote_fk
        FOREIGN KEY (customer_id, delivery_request_id, quote_id, quote_version)
        REFERENCES delivery_quote_versions (customer_id, delivery_request_id, quote_id, version),
    CONSTRAINT missions_promise_fk
        FOREIGN KEY (promise_id, promise_version, customer_id)
        REFERENCES promise_versions (promise_id, version, customer_id)
);

CREATE TABLE IF NOT EXISTS mission_confirmation_idempotency (
    customer_id text NOT NULL REFERENCES customers(customer_id),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    delivery_request_id text NOT NULL,
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    mission_id text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (customer_id, idempotency_key),
    CONSTRAINT mission_confirmation_request_fk
        FOREIGN KEY (customer_id, delivery_request_id)
        REFERENCES delivery_requests (customer_id, delivery_request_id),
    CONSTRAINT mission_confirmation_mission_fk
        FOREIGN KEY (customer_id, delivery_request_id, mission_id)
        REFERENCES missions (customer_id, delivery_request_id, mission_id)
);

CREATE INDEX IF NOT EXISTS missions_customer_created_idx
    ON missions (customer_id, created_at DESC, mission_id DESC);
