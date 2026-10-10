-- Append-only audit for Mono-controlled branch onboarding.
CREATE TABLE IF NOT EXISTS branch_operations_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^boa_[0-9a-f]{32}$'),
    branch_id text NOT NULL REFERENCES branches(branch_id),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    action text NOT NULL CHECK (action IN ('CREATED')),
    actor_id text NOT NULL CHECK (length(trim(actor_id)) > 0),
    reason text NOT NULL CHECK (length(trim(reason)) > 0),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS branch_operations_audit_history_idx
    ON branch_operations_audit (customer_id, occurred_at DESC, audit_id DESC);

CREATE OR REPLACE FUNCTION reject_branch_operations_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'branch operations audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS branch_operations_audit_no_update_delete ON branch_operations_audit;
CREATE TRIGGER branch_operations_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON branch_operations_audit
    FOR EACH ROW EXECUTE FUNCTION reject_branch_operations_audit_mutation();

DROP TRIGGER IF EXISTS branch_operations_audit_no_truncate ON branch_operations_audit;
CREATE TRIGGER branch_operations_audit_no_truncate
    BEFORE TRUNCATE ON branch_operations_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_branch_operations_audit_mutation();
