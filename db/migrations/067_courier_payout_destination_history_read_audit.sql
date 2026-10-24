-- Add auditable operations reads for payout destination verification and secret-access history.
CREATE TABLE IF NOT EXISTS courier_payout_destination_history_read_audit (
    read_audit_id text PRIMARY KEY CHECK (read_audit_id ~ '^cph_[0-9a-f]{32}$'),
    courier_id text NOT NULL CHECK (courier_id ~ '^crr_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'NOT_FOUND')),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    occurred_at timestamp with time zone NOT NULL,
    CHECK ((outcome_code = 'FOUND' AND result_count BETWEEN 0 AND 100) OR
           (outcome_code = 'NOT_FOUND' AND result_count = 0))
);

CREATE INDEX IF NOT EXISTS courier_payout_destination_history_read_audit_idx
    ON courier_payout_destination_history_read_audit
        (courier_id, occurred_at DESC, read_audit_id DESC);

DROP TRIGGER IF EXISTS courier_payout_destination_history_read_audit_immutable
    ON courier_payout_destination_history_read_audit;
CREATE TRIGGER courier_payout_destination_history_read_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_destination_history_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_payout_destination_history_read_audit_no_truncate
    ON courier_payout_destination_history_read_audit;
CREATE TRIGGER courier_payout_destination_history_read_audit_no_truncate
    BEFORE TRUNCATE ON courier_payout_destination_history_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
