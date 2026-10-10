-- Add audited policy lifecycle transitions and enforce a single active version per policy.
ALTER TABLE policy_audit DROP CONSTRAINT IF EXISTS policy_audit_action_check;
ALTER TABLE policy_audit ADD CONSTRAINT policy_audit_action_check CHECK (action IN (
    'POLICY_CREATED', 'VERSION_DRAFTED', 'VERSION_ACTIVATED', 'VERSION_DEACTIVATED', 'VERSION_RETIRED'));
CREATE UNIQUE INDEX IF NOT EXISTS policy_versions_one_active_idx ON policy_versions (policy_id) WHERE status = 'ACTIVE';
