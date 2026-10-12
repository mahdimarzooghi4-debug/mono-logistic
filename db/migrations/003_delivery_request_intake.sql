-- First customer-facing request intake. Idempotency is scoped to authenticated customer.
CREATE UNIQUE INDEX IF NOT EXISTS branches_customer_branch_identity_idx
    ON branches (customer_id, branch_id);

CREATE TABLE IF NOT EXISTS delivery_requests (
    delivery_request_id text PRIMARY KEY CHECK (delivery_request_id ~ '^drq_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    branch_id text NOT NULL,
    external_reference text NOT NULL CHECK (length(btrim(external_reference)) BETWEEN 1 AND 128),
    idempotency_key text NOT NULL CHECK (length(idempotency_key) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    request_payload jsonb NOT NULL,
    status text NOT NULL CHECK (status = 'RECEIVED'),
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT delivery_requests_customer_idempotency_unique
        UNIQUE (customer_id, idempotency_key),
    CONSTRAINT delivery_requests_customer_branch_fk
        FOREIGN KEY (customer_id, branch_id)
        REFERENCES branches (customer_id, branch_id)
);

CREATE INDEX IF NOT EXISTS delivery_requests_customer_created_idx
    ON delivery_requests (customer_id, created_at DESC);
