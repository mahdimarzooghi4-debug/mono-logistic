-- Append-only accountability for privileged courier roster, detail, and ledger reads.
CREATE TABLE IF NOT EXISTS courier_operations_read_audit (
    audit_id text PRIMARY KEY CHECK (
        length(audit_id) = 36 AND left(audit_id, 4) = 'cra_' AND
        substring(audit_id from 5) !~ '[^0-9a-f]'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    action text NOT NULL CHECK (action IN (
        'COURIER_SEARCH', 'COURIER_DETAIL', 'COURIER_AUDIT_INSPECTION')),
    courier_id text NULL REFERENCES couriers(courier_id),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 101),
    occurred_at timestamp with time zone NOT NULL,
    CHECK (
        (action IN ('COURIER_SEARCH', 'COURIER_AUDIT_INSPECTION') AND courier_id IS NULL) OR
        (action = 'COURIER_DETAIL' AND courier_id IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS courier_operations_read_audit_history_idx
    ON courier_operations_read_audit (occurred_at DESC, audit_id DESC);
CREATE INDEX IF NOT EXISTS courier_operations_read_audit_actor_idx
    ON courier_operations_read_audit (actor_id, occurred_at DESC, audit_id DESC);

DROP TRIGGER IF EXISTS courier_operations_read_audit_immutable ON courier_operations_read_audit;
CREATE TRIGGER courier_operations_read_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_operations_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operations_read_audit_no_truncate ON courier_operations_read_audit;
CREATE TRIGGER courier_operations_read_audit_no_truncate
    BEFORE TRUNCATE ON courier_operations_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
