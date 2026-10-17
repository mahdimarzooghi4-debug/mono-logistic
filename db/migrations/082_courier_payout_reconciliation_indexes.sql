-- Support scoped payout-ledger reconciliation lookups by withdrawal reference.
CREATE INDEX IF NOT EXISTS ledger_transactions_withdrawal_payout_reference_idx
    ON ledger_transactions (reference_id, ledger_transaction_id)
    WHERE reference_type = 'WITHDRAWAL_PAYOUT';
