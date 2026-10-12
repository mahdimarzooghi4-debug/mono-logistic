-- Append-only operational audit for sensitive customer service configuration changes.
CREATE TABLE IF NOT EXISTS customer_service_configuration_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^sca_[0-9a-f]{32}$'),
    service_configuration_id text NOT NULL
        REFERENCES customer_service_configurations(service_configuration_id),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    service_code text NOT NULL CHECK (service_code ~ '^[A-Z][A-Z0-9_]{0,63}$'),
    version integer NOT NULL CHECK (version > 0),
    action text NOT NULL CHECK (action IN ('DRAFT_CREATED', 'ACTIVATED')),
    actor_id text NOT NULL CHECK (length(trim(actor_id)) > 0),
    reason text NOT NULL CHECK (length(trim(reason)) > 0),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS customer_service_configuration_audit_history_idx
    ON customer_service_configuration_audit (customer_id, service_code, occurred_at DESC);

CREATE OR REPLACE FUNCTION reject_customer_service_configuration_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'customer service configuration audit is append-only';
END;
$$;

CREATE OR REPLACE TRIGGER customer_service_configuration_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON customer_service_configuration_audit
    FOR EACH ROW EXECUTE FUNCTION reject_customer_service_configuration_audit_mutation();

CREATE OR REPLACE TRIGGER customer_service_configuration_audit_no_truncate
    BEFORE TRUNCATE ON customer_service_configuration_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_customer_service_configuration_audit_mutation();
