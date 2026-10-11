-- Immutable, tenant-scoped Quote snapshots. Expiry is supplied by the caller.
CREATE TABLE IF NOT EXISTS delivery_quote_versions (
    customer_id text NOT NULL,
    delivery_request_id text NOT NULL,
    quote_id text NOT NULL CHECK (length(btrim(quote_id)) BETWEEN 1 AND 128),
    version integer NOT NULL CHECK (version > 0),
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL CHECK (expires_at > issued_at),
    snapshot_hash text NOT NULL CHECK (snapshot_hash ~ '^[0-9a-f]{64}$'),
    snapshot jsonb NOT NULL CHECK (jsonb_typeof(snapshot) = 'object'),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (customer_id, delivery_request_id, version),
    CONSTRAINT delivery_quote_versions_customer_quote_version_unique
        UNIQUE (customer_id, quote_id, version),
    CONSTRAINT delivery_quote_versions_customer_fk
        FOREIGN KEY (customer_id) REFERENCES customers(customer_id),
    CONSTRAINT delivery_quote_versions_request_fk
        FOREIGN KEY (customer_id, delivery_request_id)
        REFERENCES delivery_requests (customer_id, delivery_request_id)
);

CREATE INDEX IF NOT EXISTS delivery_quote_versions_customer_request_created_idx
    ON delivery_quote_versions (customer_id, delivery_request_id, created_at DESC, version DESC);
