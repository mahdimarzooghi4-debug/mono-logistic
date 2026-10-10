-- Customer-scoped external branch IDs for system-to-Mono branch mapping.
CREATE UNIQUE INDEX IF NOT EXISTS branches_customer_branch_pair_unique
    ON branches (branch_id, customer_id);

CREATE TABLE IF NOT EXISTS branch_external_identifiers (
    external_identifier_id text PRIMARY KEY
        CHECK (external_identifier_id ~ '^bxi_[0-9a-f]{32}$'),
    branch_id text NOT NULL,
    customer_id text NOT NULL REFERENCES customers(customer_id),
    namespace text NOT NULL
        CHECK (namespace ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    external_id text NOT NULL
        CHECK (length(btrim(external_id)) BETWEEN 1 AND 256),
    status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('ACTIVE', 'RETIRED')),
    created_at timestamp with time zone NOT NULL,
    retired_at timestamp with time zone NULL,
    CONSTRAINT branch_external_identifiers_retirement_consistent
        CHECK ((status = 'ACTIVE' AND retired_at IS NULL) OR
               (status = 'RETIRED' AND retired_at IS NOT NULL)),
    CONSTRAINT branch_external_identifiers_branch_customer_fk
        FOREIGN KEY (branch_id, customer_id)
        REFERENCES branches(branch_id, customer_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS branch_external_identifiers_active_unique
    ON branch_external_identifiers (customer_id, namespace, external_id)
    WHERE status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS branch_external_identifiers_branch_idx
    ON branch_external_identifiers (customer_id, branch_id, status, namespace, external_id);

CREATE TABLE IF NOT EXISTS branch_external_identifier_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^bxia_[0-9a-f]{32}$'),
    external_identifier_id text NOT NULL
        REFERENCES branch_external_identifiers(external_identifier_id),
    branch_id text NOT NULL,
    customer_id text NOT NULL REFERENCES customers(customer_id),
    namespace text NOT NULL CHECK (namespace ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    external_id text NOT NULL CHECK (length(btrim(external_id)) BETWEEN 1 AND 256),
    action text NOT NULL CHECK (action IN ('ADDED', 'RETIRED')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) > 0),
    reason text NOT NULL CHECK (length(btrim(reason)) > 0),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT branch_external_identifier_audit_branch_customer_fk
        FOREIGN KEY (branch_id, customer_id)
        REFERENCES branches(branch_id, customer_id)
);

CREATE INDEX IF NOT EXISTS branch_external_identifier_audit_history_idx
    ON branch_external_identifier_audit
       (customer_id, branch_id, occurred_at, audit_id);

CREATE OR REPLACE FUNCTION reject_branch_external_identifier_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'branch external identifier audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS branch_external_identifier_audit_no_update_delete
    ON branch_external_identifier_audit;
CREATE TRIGGER branch_external_identifier_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON branch_external_identifier_audit
    FOR EACH ROW EXECUTE FUNCTION reject_branch_external_identifier_audit_mutation();

DROP TRIGGER IF EXISTS branch_external_identifier_audit_no_truncate
    ON branch_external_identifier_audit;
CREATE TRIGGER branch_external_identifier_audit_no_truncate
    BEFORE TRUNCATE ON branch_external_identifier_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_branch_external_identifier_audit_mutation();
