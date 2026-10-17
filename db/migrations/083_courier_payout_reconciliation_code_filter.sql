-- Record and validate the optional reconciliation classification selected by each report read.
ALTER TABLE courier_payout_reconciliation_read_audit
    ADD COLUMN IF NOT EXISTS reconciliation_code_filter text NULL
        CHECK (reconciliation_code_filter IS NULL OR reconciliation_code_filter IN (
            'MATCHED', 'MISSING_PAYOUT', 'UNEXPECTED_PAYOUT', 'MULTIPLE_PAYOUTS', 'LEDGER_MISMATCH'));
