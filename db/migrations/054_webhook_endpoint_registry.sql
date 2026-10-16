-- Customer webhook endpoint registrations. Signing keys are authenticated ciphertext;
-- the application key must come from the configured secret store.
CREATE TABLE IF NOT EXISTS webhook_endpoints (
    endpoint_id text PRIMARY KEY CHECK (endpoint_id ~ '^whk_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    url text NOT NULL CHECK (length(url) BETWEEN 1 AND 2048),
    events text[] NOT NULL CHECK (cardinality(events) BETWEEN 1 AND 14),
    secret_ciphertext text NOT NULL CHECK (secret_ciphertext LIKE 'v1:%'),
    status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'DISABLED')),
    created_at timestamp with time zone NOT NULL,
    disabled_at timestamp with time zone NULL,
    CONSTRAINT webhook_endpoints_customer_id_unique UNIQUE (customer_id, endpoint_id)
);

CREATE INDEX IF NOT EXISTS webhook_endpoints_active_customer_idx
    ON webhook_endpoints (customer_id, created_at DESC, endpoint_id DESC)
    WHERE status = 'ACTIVE';
