-- Idempotency keys and their first Quote version commit atomically.
CREATE TABLE IF NOT EXISTS delivery_quote_issuance_idempotency (
    customer_id text NOT NULL,
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 200),
    delivery_request_id text NOT NULL,
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    quote_id text NOT NULL,
    version integer NOT NULL CHECK (version = 1),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (customer_id, idempotency_key),
    CONSTRAINT delivery_quote_issuance_idempotency_customer_fk
        FOREIGN KEY (customer_id) REFERENCES customers(customer_id),
    CONSTRAINT delivery_quote_issuance_idempotency_request_fk
        FOREIGN KEY (customer_id, delivery_request_id)
        REFERENCES delivery_requests(customer_id, delivery_request_id),
    CONSTRAINT delivery_quote_issuance_idempotency_quote_fk
        FOREIGN KEY (customer_id, delivery_request_id, version)
        REFERENCES delivery_quote_versions(customer_id, delivery_request_id, version)
);
