-- Accountability for privileged, cross-tenant webhook queue summaries.
CREATE TABLE IF NOT EXISTS webhook_operations_read_audit (
    audit_id text PRIMARY KEY CHECK (audit_id ~ '^woa_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(actor_id) BETWEEN 1 AND 128),
    action text NOT NULL CHECK (action = 'QUEUE_SUMMARY'),
    result_count bigint NOT NULL CHECK (result_count >= 0),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS webhook_operations_read_audit_history_idx
    ON webhook_operations_read_audit (occurred_at DESC, audit_id DESC);

CREATE OR REPLACE FUNCTION reject_webhook_operations_read_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'webhook operations read audit is append-only';
END;
$$;

DROP TRIGGER IF EXISTS webhook_operations_read_audit_immutable ON webhook_operations_read_audit;
CREATE TRIGGER webhook_operations_read_audit_immutable
    BEFORE UPDATE OR DELETE ON webhook_operations_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_webhook_operations_read_audit_mutation();

DROP TRIGGER IF EXISTS webhook_operations_read_audit_no_truncate ON webhook_operations_read_audit;
CREATE TRIGGER webhook_operations_read_audit_no_truncate
    BEFORE TRUNCATE ON webhook_operations_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_webhook_operations_read_audit_mutation();
