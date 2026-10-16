-- Support time-windowed courier ledger statements and aggregate reads.
CREATE INDEX IF NOT EXISTS ledger_transactions_occurred_at_history_idx
    ON ledger_transactions (occurred_at DESC, ledger_transaction_id);
