-- Record audited retirement of active customer service configurations.
ALTER TABLE customer_service_configuration_audit
    DROP CONSTRAINT IF EXISTS customer_service_configuration_audit_action_check;

ALTER TABLE customer_service_configuration_audit
    ADD CONSTRAINT customer_service_configuration_audit_action_check
    CHECK (action IN ('DRAFT_CREATED', 'ACTIVATED', 'RETIRED'));
