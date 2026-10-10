-- Versioned, Mono-admin-authored policy drafts. Activation and evaluation are separate work.
CREATE TABLE IF NOT EXISTS policies (
    policy_id text PRIMARY KEY CHECK (policy_id ~ '^pol_[0-9a-f]{32}$'),
    policy_type text NOT NULL CHECK (policy_type ~ '^[A-Z][A-Z0-9_]{0,63}$'),
    scope text NOT NULL CHECK (scope IN (
        'PLATFORM', 'SERVICE', 'CUSTOMER', 'BRANCH', 'ZONE', 'CONTRACT', 'MISSION', 'RUNTIME')),
    scope_id text NULL,
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL CHECK (length(btrim(created_by)) > 0),
    CHECK ((scope = 'PLATFORM' AND scope_id IS NULL) OR
           (scope <> 'PLATFORM' AND length(btrim(scope_id)) > 0))
);

CREATE TABLE IF NOT EXISTS policy_versions (
    policy_id text NOT NULL REFERENCES policies(policy_id),
    version_number integer NOT NULL CHECK (version_number > 0),
    enforcement_type text NOT NULL CHECK (enforcement_type IN (
        'HARD', 'OPTIMIZATION', 'COMMERCIAL', 'CONFIGURATION',
        'CONTRACTUAL', 'WORKFLOW', 'FINANCIAL', 'RISK')),
    priority integer NOT NULL,
    status text NOT NULL DEFAULT 'DRAFT'
        CHECK (status IN ('DRAFT', 'ACTIVE', 'INACTIVE', 'RETIRED')),
    effective_from timestamp with time zone NULL,
    effective_to timestamp with time zone NULL,
    definition jsonb NOT NULL CHECK (jsonb_typeof(definition) = 'object'),
    created_at timestamp with time zone NOT NULL,
    created_by text NOT NULL CHECK (length(btrim(created_by)) > 0),
    PRIMARY KEY (policy_id, version_number),
    CHECK (effective_to IS NULL OR effective_from IS NULL OR effective_to > effective_from)
);

CREATE INDEX IF NOT EXISTS policy_versions_status_idx
    ON policy_versions (status, policy_id, version_number DESC);

CREATE TABLE IF NOT EXISTS policy_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^pa_[0-9a-f]{32}$'),
    policy_id text NOT NULL,
    version_number integer NOT NULL,
    action text NOT NULL CHECK (action IN ('POLICY_CREATED', 'VERSION_DRAFTED')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) > 0),
    reason text NOT NULL CHECK (length(btrim(reason)) > 0),
    policy_snapshot jsonb NOT NULL CHECK (jsonb_typeof(policy_snapshot) = 'object'),
    occurred_at timestamp with time zone NOT NULL,
    FOREIGN KEY (policy_id, version_number)
        REFERENCES policy_versions(policy_id, version_number)
);

CREATE INDEX IF NOT EXISTS policy_audit_history_idx
    ON policy_audit (policy_id, occurred_at, audit_id);

CREATE OR REPLACE FUNCTION reject_policy_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'policy audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS policy_audit_no_update_delete ON policy_audit;
CREATE TRIGGER policy_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON policy_audit
    FOR EACH ROW EXECUTE FUNCTION reject_policy_audit_mutation();

DROP TRIGGER IF EXISTS policy_audit_no_truncate ON policy_audit;
CREATE TRIGGER policy_audit_no_truncate
    BEFORE TRUNCATE ON policy_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_policy_audit_mutation();
