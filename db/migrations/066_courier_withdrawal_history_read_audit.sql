-- Audit every privileged lookup of a courier withdrawal lifecycle timeline.
CREATE TABLE IF NOT EXISTS courier_withdrawal_history_read_audit (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cwr_[0-9a-f]{32}$'),
    withdrawal_id text NOT NULL CHECK (withdrawal_id ~ '^wdr_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'NOT_FOUND')),
    event_count integer NOT NULL CHECK (event_count BETWEEN 0 AND 4),
    occurred_at timestamp with time zone NOT NULL,
    CHECK ((outcome_code = 'FOUND' AND event_count BETWEEN 1 AND 4) OR
           (outcome_code = 'NOT_FOUND' AND event_count = 0))
);

CREATE INDEX IF NOT EXISTS courier_withdrawal_history_read_audit_history_idx
    ON courier_withdrawal_history_read_audit (withdrawal_id, occurred_at DESC, read_audit_id DESC);

DROP TRIGGER IF EXISTS courier_withdrawal_history_read_audit_immutable
    ON courier_withdrawal_history_read_audit;
CREATE TRIGGER courier_withdrawal_history_read_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_history_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_withdrawal_history_read_audit_no_truncate
    ON courier_withdrawal_history_read_audit;
CREATE TRIGGER courier_withdrawal_history_read_audit_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_history_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
