-- Append-only audit for Mono administrator revocation of customer API keys.
CREATE TABLE IF NOT EXISTS customer_api_key_operations_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^kao_[0-9a-f]{32}$'),
    key_id text NOT NULL REFERENCES customer_api_keys(key_id),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    action text NOT NULL CHECK (action IN ('REVOKED')),
    actor_id text NOT NULL CHECK (length(trim(actor_id)) > 0),
    reason text NOT NULL CHECK (length(trim(reason)) > 0),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS customer_api_key_operations_audit_history_idx
    ON customer_api_key_operations_audit (customer_id, key_id, occurred_at DESC, audit_id DESC);

CREATE OR REPLACE FUNCTION reject_customer_api_key_operations_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'customer API key operations audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS customer_api_key_operations_audit_no_update_delete
    ON customer_api_key_operations_audit;
CREATE TRIGGER customer_api_key_operations_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON customer_api_key_operations_audit
    FOR EACH ROW EXECUTE FUNCTION reject_customer_api_key_operations_audit_mutation();

DROP TRIGGER IF EXISTS customer_api_key_operations_audit_no_truncate
    ON customer_api_key_operations_audit;
CREATE TRIGGER customer_api_key_operations_audit_no_truncate
    BEFORE TRUNCATE ON customer_api_key_operations_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_customer_api_key_operations_audit_mutation();
