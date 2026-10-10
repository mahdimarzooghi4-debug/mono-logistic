-- Provision through a trusted operational workflow; never persist the raw key.
CREATE TABLE IF NOT EXISTS customer_api_keys (
    key_id text PRIMARY KEY CHECK (key_id ~ '^key_[0-9a-f]{16}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    secret_hash char(64) NOT NULL CHECK (secret_hash ~ '^[0-9a-f]{64}$'),
    scopes text[] NOT NULL CHECK (cardinality(scopes) > 0),
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone NULL,
    CONSTRAINT customer_api_keys_expiry_after_creation CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS customer_api_keys_customer_id_idx ON customer_api_keys(customer_id);
