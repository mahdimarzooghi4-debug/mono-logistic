-- Generic external customer identifiers; namespaces identify a source/integration account.
CREATE TABLE IF NOT EXISTS customer_external_identifiers (
    external_identifier_id text PRIMARY KEY
        CHECK (external_identifier_id ~ '^cei_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    namespace text NOT NULL
        CHECK (namespace ~ '^[a-z0-9][a-z0-9._-]{0,63}$'),
    external_id text NOT NULL
        CHECK (length(btrim(external_id)) BETWEEN 1 AND 256),
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT customer_external_identifiers_namespace_external_id_key
        UNIQUE (namespace, external_id)
);

CREATE INDEX IF NOT EXISTS customer_external_identifiers_customer_history_idx
    ON customer_external_identifiers (customer_id, namespace, external_id);

CREATE TABLE IF NOT EXISTS customer_external_identifier_audit (
    audit_id text PRIMARY KEY
        CHECK (audit_id ~ '^ceia_[0-9a-f]{32}$'),
    external_identifier_id text NOT NULL
        REFERENCES customer_external_identifiers(external_identifier_id),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    namespace text NOT NULL,
    external_id text NOT NULL,
    action text NOT NULL CHECK (action IN ('ADDED')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) > 0),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 1000),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS customer_external_identifier_audit_history_idx
    ON customer_external_identifier_audit (customer_id, occurred_at, audit_id);

CREATE OR REPLACE FUNCTION reject_customer_external_identifier_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'customer external identifier audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS customer_external_identifier_audit_no_update_delete
    ON customer_external_identifier_audit;
CREATE TRIGGER customer_external_identifier_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON customer_external_identifier_audit
    FOR EACH ROW EXECUTE FUNCTION reject_customer_external_identifier_audit_mutation();

DROP TRIGGER IF EXISTS customer_external_identifier_audit_no_truncate
    ON customer_external_identifier_audit;
CREATE TRIGGER customer_external_identifier_audit_no_truncate
    BEFORE TRUNCATE ON customer_external_identifier_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_customer_external_identifier_audit_mutation();
