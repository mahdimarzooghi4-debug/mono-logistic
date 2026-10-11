-- Persist the current Promise, immutable Promise versions, and their transactional outbox events.
CREATE UNIQUE INDEX IF NOT EXISTS delivery_requests_customer_id_identity_idx
    ON delivery_requests (customer_id, delivery_request_id);

CREATE TABLE IF NOT EXISTS promises (
    promise_id text PRIMARY KEY CHECK (promise_id ~ '^prm_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    delivery_request_id text NOT NULL,
    current_version integer NOT NULL CHECK (current_version > 0),
    latest_decision_id text NOT NULL,
    committed_delivery_at timestamp with time zone NOT NULL,
    outcome text NOT NULL CHECK (outcome IN ('ACCEPTED', 'ALTERNATIVE_PROMISE')),
    safety_buffer_ticks bigint NOT NULL CHECK (safety_buffer_ticks >= 0),
    safety_buffer_policy_version text NOT NULL CHECK (length(btrim(safety_buffer_policy_version)) > 0),
    policy_versions jsonb NOT NULL CHECK (jsonb_typeof(policy_versions) = 'object'),
    reason_codes jsonb NOT NULL CHECK (jsonb_typeof(reason_codes) = 'array'),
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT promises_customer_request_unique UNIQUE (customer_id, delivery_request_id),
    CONSTRAINT promises_customer_request_fk
        FOREIGN KEY (customer_id, delivery_request_id)
        REFERENCES delivery_requests (customer_id, delivery_request_id)
);

CREATE TABLE IF NOT EXISTS promise_versions (
    promise_id text NOT NULL REFERENCES promises(promise_id),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    version integer NOT NULL CHECK (version > 0),
    decision_id text NOT NULL,
    proposal_hash text NOT NULL CHECK (proposal_hash ~ '^[0-9a-f]{64}$'),
    proposal jsonb NOT NULL CHECK (jsonb_typeof(proposal) = 'object'),
    event_id text NOT NULL UNIQUE,
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (promise_id, version),
    CONSTRAINT promise_versions_customer_decision_unique UNIQUE (customer_id, decision_id)
);

CREATE TABLE IF NOT EXISTS outbox_events (
    event_id text PRIMARY KEY CHECK (event_id ~ '^evt_[0-9a-f]{32}$'),
    event_type text NOT NULL CHECK (event_type IN ('promise.committed', 'promise.revised')),
    event_version integer NOT NULL CHECK (event_version > 0),
    occurred_at timestamp with time zone NOT NULL,
    producer text NOT NULL,
    correlation_id text NOT NULL,
    causation_id text NULL,
    customer_id text NOT NULL REFERENCES customers(customer_id),
    aggregate_type text NOT NULL,
    aggregate_id text NOT NULL,
    sequence bigint NOT NULL CHECK (sequence > 0),
    data jsonb NOT NULL CHECK (jsonb_typeof(data) = 'object'),
    created_at timestamp with time zone NOT NULL,
    published_at timestamp with time zone NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    CONSTRAINT outbox_events_aggregate_sequence_unique UNIQUE (aggregate_type, aggregate_id, sequence)
);

CREATE INDEX IF NOT EXISTS outbox_events_unpublished_idx
    ON outbox_events (created_at, event_id)
    WHERE published_at IS NULL;
