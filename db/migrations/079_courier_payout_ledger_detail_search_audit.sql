-- Audit every administrator search/read of the payout ledger drilldown history.
CREATE TABLE IF NOT EXISTS courier_payout_ledger_detail_search_read_audit (
    search_audit_id text PRIMARY KEY CHECK (search_audit_id ~ '^cps_[0-9a-f]{32}$'),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    actor_filter text NULL CHECK (actor_filter IS NULL OR length(btrim(actor_filter)) BETWEEN 1 AND 128),
    withdrawal_id_filter text NULL CHECK (withdrawal_id_filter IS NULL OR withdrawal_id_filter ~ '^wdr_[0-9a-f]{32}$'),
    outcome_filter text NULL CHECK (outcome_filter IS NULL OR outcome_filter IN ('FOUND', 'PAYOUT_NOT_POSTED', 'WITHDRAWAL_NOT_FOUND')),
    occurred_from_filter timestamp with time zone NOT NULL,
    occurred_to_filter timestamp with time zone NOT NULL,
    cursor_occurred_at timestamp with time zone NULL,
    cursor_read_audit_id text NULL CHECK (cursor_read_audit_id IS NULL OR cursor_read_audit_id ~ '^cpl_[0-9a-f]{32}$'),
    page_size integer NOT NULL CHECK (page_size BETWEEN 1 AND 100),
    result_count integer NOT NULL CHECK (result_count BETWEEN 0 AND 100),
    outcome_code text NOT NULL CHECK (outcome_code IN ('FOUND', 'EMPTY')),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT courier_payout_ledger_detail_search_read_audit_range_check CHECK (occurred_from_filter < occurred_to_filter),
    CONSTRAINT courier_payout_ledger_detail_search_read_audit_cursor_check CHECK ((cursor_occurred_at IS NULL) = (cursor_read_audit_id IS NULL))
);
CREATE INDEX IF NOT EXISTS courier_payout_ledger_detail_search_read_audit_history_idx
    ON courier_payout_ledger_detail_search_read_audit (actor_id, occurred_at DESC, search_audit_id DESC);
DROP TRIGGER IF EXISTS courier_payout_ledger_detail_search_read_audit_immutable ON courier_payout_ledger_detail_search_read_audit;
CREATE TRIGGER courier_payout_ledger_detail_search_read_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_ledger_detail_search_read_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_ledger_detail_search_read_audit_no_truncate ON courier_payout_ledger_detail_search_read_audit;
CREATE TRIGGER courier_payout_ledger_detail_search_read_audit_no_truncate
    BEFORE TRUNCATE ON courier_payout_ledger_detail_search_read_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
