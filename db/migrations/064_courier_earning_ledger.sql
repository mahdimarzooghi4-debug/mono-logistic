-- Double-entry, append-only ledger for delivery earnings. No payout integration is enabled here.
CREATE TABLE IF NOT EXISTS ledger_accounts (
    ledger_account_id text PRIMARY KEY CHECK (ledger_account_id ~ '^lac_[0-9a-f]{32}$'),
    owner_type text NOT NULL CHECK (owner_type IN ('CUSTOMER', 'COURIER', 'MONO')),
    owner_id text NOT NULL CHECK (length(btrim(owner_id)) BETWEEN 1 AND 128),
    account_code text NOT NULL CHECK (account_code IN (
        'CUSTOMER_RECEIVABLE', 'COURIER_AVAILABLE', 'MONO_COMMISSION_REVENUE', 'VAT_PAYABLE')),
    currency text NOT NULL CHECK (currency = 'TOMAN'),
    created_at timestamp with time zone NOT NULL,
    UNIQUE (owner_type, owner_id, account_code, currency),
    CHECK ((owner_type = 'CUSTOMER' AND account_code = 'CUSTOMER_RECEIVABLE') OR
           (owner_type = 'COURIER' AND account_code = 'COURIER_AVAILABLE') OR
           (owner_type = 'MONO' AND account_code IN ('MONO_COMMISSION_REVENUE', 'VAT_PAYABLE')))
);

CREATE TABLE IF NOT EXISTS ledger_transactions (
    ledger_transaction_id text PRIMARY KEY CHECK (ledger_transaction_id ~ '^ltx_[0-9a-f]{32}$'),
    reference_type text NOT NULL CHECK (reference_type = 'MISSION_EARNING'),
    reference_id text NOT NULL CHECK (reference_id ~ '^mis_[0-9a-f]{32}$'),
    idempotency_key text NOT NULL UNIQUE CHECK (length(idempotency_key) BETWEEN 1 AND 160),
    currency text NOT NULL CHECK (currency = 'TOMAN'),
    occurred_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    UNIQUE (reference_type, reference_id)
);

CREATE TABLE IF NOT EXISTS ledger_entries (
    ledger_entry_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ledger_transaction_id text NOT NULL REFERENCES ledger_transactions(ledger_transaction_id),
    ledger_account_id text NOT NULL REFERENCES ledger_accounts(ledger_account_id),
    debit_amount bigint NOT NULL DEFAULT 0 CHECK (debit_amount >= 0),
    credit_amount bigint NOT NULL DEFAULT 0 CHECK (credit_amount >= 0),
    created_at timestamp with time zone NOT NULL,
    UNIQUE (ledger_transaction_id, ledger_account_id),
    CHECK ((debit_amount > 0 AND credit_amount = 0) OR
           (credit_amount > 0 AND debit_amount = 0))
);

CREATE INDEX IF NOT EXISTS ledger_entries_account_history_idx
    ON ledger_entries (ledger_account_id, ledger_entry_id DESC);

CREATE OR REPLACE FUNCTION reject_ledger_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'financial ledger records are append-only';
END;
$$;

DROP TRIGGER IF EXISTS ledger_accounts_immutable ON ledger_accounts;
CREATE TRIGGER ledger_accounts_immutable
    BEFORE UPDATE OR DELETE ON ledger_accounts
    FOR EACH ROW EXECUTE FUNCTION reject_ledger_mutation();
DROP TRIGGER IF EXISTS ledger_accounts_no_truncate ON ledger_accounts;
CREATE TRIGGER ledger_accounts_no_truncate
    BEFORE TRUNCATE ON ledger_accounts
    FOR EACH STATEMENT EXECUTE FUNCTION reject_ledger_mutation();

DROP TRIGGER IF EXISTS ledger_transactions_immutable ON ledger_transactions;
CREATE TRIGGER ledger_transactions_immutable
    BEFORE UPDATE OR DELETE ON ledger_transactions
    FOR EACH ROW EXECUTE FUNCTION reject_ledger_mutation();
DROP TRIGGER IF EXISTS ledger_transactions_no_truncate ON ledger_transactions;
CREATE TRIGGER ledger_transactions_no_truncate
    BEFORE TRUNCATE ON ledger_transactions
    FOR EACH STATEMENT EXECUTE FUNCTION reject_ledger_mutation();

DROP TRIGGER IF EXISTS ledger_entries_immutable ON ledger_entries;
CREATE TRIGGER ledger_entries_immutable
    BEFORE UPDATE OR DELETE ON ledger_entries
    FOR EACH ROW EXECUTE FUNCTION reject_ledger_mutation();
DROP TRIGGER IF EXISTS ledger_entries_no_truncate ON ledger_entries;
CREATE TRIGGER ledger_entries_no_truncate
    BEFORE TRUNCATE ON ledger_entries
    FOR EACH STATEMENT EXECUTE FUNCTION reject_ledger_mutation();

CREATE OR REPLACE FUNCTION validate_ledger_transaction_balance()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    target_transaction_id text;
    entry_count bigint;
    total_debits numeric;
    total_credits numeric;
BEGIN
    target_transaction_id := NEW.ledger_transaction_id;
    SELECT COUNT(*), COALESCE(SUM(debit_amount), 0), COALESCE(SUM(credit_amount), 0)
      INTO entry_count, total_debits, total_credits
    FROM ledger_entries
    WHERE ledger_transaction_id = target_transaction_id;
    IF entry_count < 2 OR total_debits <= 0 OR total_debits <> total_credits THEN
        RAISE EXCEPTION 'ledger transaction % is not balanced', target_transaction_id;
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS ledger_transaction_balance_check ON ledger_transactions;
CREATE CONSTRAINT TRIGGER ledger_transaction_balance_check
    AFTER INSERT ON ledger_transactions
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION validate_ledger_transaction_balance();
DROP TRIGGER IF EXISTS ledger_entry_balance_check ON ledger_entries;
CREATE CONSTRAINT TRIGGER ledger_entry_balance_check
    AFTER INSERT ON ledger_entries
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION validate_ledger_transaction_balance();

CREATE OR REPLACE FUNCTION validate_ledger_entry_currency()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    transaction_currency text;
    account_currency text;
BEGIN
    SELECT currency INTO transaction_currency FROM ledger_transactions
    WHERE ledger_transaction_id = NEW.ledger_transaction_id;
    SELECT currency INTO account_currency FROM ledger_accounts
    WHERE ledger_account_id = NEW.ledger_account_id;
    IF transaction_currency IS NULL OR account_currency IS NULL OR
       transaction_currency <> account_currency THEN
        RAISE EXCEPTION 'ledger transaction and account currencies must match';
    END IF;
    RETURN NEW;
END;
$$;
DROP TRIGGER IF EXISTS ledger_entry_currency_check ON ledger_entries;
CREATE TRIGGER ledger_entry_currency_check
    BEFORE INSERT ON ledger_entries
    FOR EACH ROW EXECUTE FUNCTION validate_ledger_entry_currency();
