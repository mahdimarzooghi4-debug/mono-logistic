-- Append-only accountability for current and historical Route plan inspections.
CREATE TABLE IF NOT EXISTS route_plan_operations_read_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^rpa_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    route_id text NULL CHECK (route_id IS NULL OR length(btrim(route_id)) BETWEEN 1 AND 128),
    action text NOT NULL CHECK (action IN (
        'ROUTE_PLAN_CURRENT_READ', 'ROUTE_PLAN_VERSION_READ', 'ROUTE_PLAN_AUDIT_INSPECTION')),
    version integer NULL CHECK (version IS NULL OR version > 0),
    outcome text NOT NULL CHECK (outcome IN ('FOUND', 'NOT_FOUND', 'EMPTY')),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT route_plan_operations_read_audit_scope_check CHECK (
        (action = 'ROUTE_PLAN_CURRENT_READ' AND route_id IS NOT NULL AND
            ((outcome = 'FOUND' AND version IS NOT NULL AND result_count = 1) OR
             (outcome = 'NOT_FOUND' AND version IS NULL AND result_count = 0))) OR
        (action = 'ROUTE_PLAN_VERSION_READ' AND route_id IS NOT NULL AND version IS NOT NULL AND
            ((outcome = 'FOUND' AND result_count = 1) OR
             (outcome = 'NOT_FOUND' AND result_count = 0))) OR
        (action = 'ROUTE_PLAN_AUDIT_INSPECTION' AND route_id IS NULL AND version IS NULL AND
            ((outcome = 'FOUND' AND result_count BETWEEN 1 AND 100) OR
             (outcome = 'EMPTY' AND result_count = 0)))
    )
);
CREATE INDEX IF NOT EXISTS route_plan_operations_read_audit_history_idx
    ON route_plan_operations_read_audit (occurred_at DESC, audit_id DESC);
CREATE INDEX IF NOT EXISTS route_plan_operations_read_audit_route_idx
    ON route_plan_operations_read_audit (route_id, occurred_at DESC, audit_id DESC);

CREATE OR REPLACE FUNCTION reject_route_plan_read_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'route plan read audit is append-only';
END;
$$;
DROP TRIGGER IF EXISTS route_plan_operations_read_audit_immutable
    ON route_plan_operations_read_audit;
CREATE TRIGGER route_plan_operations_read_audit_immutable
    BEFORE UPDATE OR DELETE ON route_plan_operations_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_route_plan_read_audit_mutation();
DROP TRIGGER IF EXISTS route_plan_operations_read_audit_no_truncate
    ON route_plan_operations_read_audit;
CREATE TRIGGER route_plan_operations_read_audit_no_truncate
    BEFORE TRUNCATE ON route_plan_operations_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_route_plan_read_audit_mutation();
