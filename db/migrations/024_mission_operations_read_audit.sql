-- Audit privileged Mission search, detail, and timeline reads without persisting search terms.
CREATE TABLE IF NOT EXISTS mission_operations_read_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^mra_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(trim(actor_id)) > 0),
    action text NOT NULL CHECK (action IN ('MISSION_SEARCH', 'MISSION_DETAIL', 'MISSION_TIMELINE')),
    mission_id text NULL CHECK (mission_id IS NULL OR mission_id ~ '^mis_[0-9a-f]{32}$'),
    result_count integer NOT NULL CHECK (result_count >= 0),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS mission_operations_read_audit_history_idx
    ON mission_operations_read_audit (occurred_at DESC, audit_id DESC);
CREATE INDEX IF NOT EXISTS mission_operations_read_audit_mission_idx
    ON mission_operations_read_audit (mission_id, occurred_at DESC, audit_id DESC)
    WHERE mission_id IS NOT NULL;

CREATE OR REPLACE FUNCTION reject_mission_operations_read_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Mission operations read audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS mission_operations_read_audit_no_update_delete
    ON mission_operations_read_audit;
CREATE TRIGGER mission_operations_read_audit_no_update_delete
    BEFORE UPDATE OR DELETE ON mission_operations_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_mission_operations_read_audit_mutation();

DROP TRIGGER IF EXISTS mission_operations_read_audit_no_truncate
    ON mission_operations_read_audit;
CREATE TRIGGER mission_operations_read_audit_no_truncate
    BEFORE TRUNCATE ON mission_operations_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_mission_operations_read_audit_mutation();
