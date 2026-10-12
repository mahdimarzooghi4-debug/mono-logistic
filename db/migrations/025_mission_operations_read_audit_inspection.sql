-- Allow audited inspection of the read-audit ledger and index it for actor-scoped history.
ALTER TABLE mission_operations_read_audit
    DROP CONSTRAINT IF EXISTS mission_operations_read_audit_action_check;
ALTER TABLE mission_operations_read_audit
    ADD CONSTRAINT mission_operations_read_audit_action_check
    CHECK (action IN (
        'MISSION_SEARCH', 'MISSION_DETAIL', 'MISSION_TIMELINE', 'MISSION_AUDIT_INSPECTION'));

CREATE INDEX IF NOT EXISTS mission_operations_read_audit_actor_idx
    ON mission_operations_read_audit (actor_id, occurred_at DESC, audit_id DESC);
