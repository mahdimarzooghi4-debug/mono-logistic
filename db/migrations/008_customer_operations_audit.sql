-- Append-only audit for Mono-controlled customer onboarding and activation.
CREATE TABLE IF NOT EXISTS customer_operations_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^coa_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    action text NOT NULL CHECK (action IN ('CREATED', 'ACTIVATED')),
    actor_id text NOT NULL CHECK (length(trim(actor_id)) > 0),
    reason text NOT NULL CHECK (length(trim(reason)) > 0),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS customer_operations_audit_history_idx
    ON customer_operations_audit (customer_id, occurred_at DESC, audit_id DESC);

CREATE OR REPLACE FUNCTION reject_customer_operations_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'customer operations audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS customer_operations_audit_no_update_delete ON customer_operations_audit;
CREATE TRIGGER customer_operations_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON customer_operations_audit
    FOR EACH ROW EXECUTE FUNCTION reject_customer_operations_audit_mutation();

DROP TRIGGER IF EXISTS customer_operations_audit_no_truncate ON customer_operations_audit;
CREATE TRIGGER customer_operations_audit_no_truncate
    BEFORE TRUNCATE ON customer_operations_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_customer_operations_audit_mutation();
