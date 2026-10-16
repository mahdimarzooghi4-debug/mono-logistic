-- Extend the courier ledger to record completed manual payouts.
ALTER TABLE ledger_accounts
    DROP CONSTRAINT IF EXISTS ledger_accounts_account_code_check;
ALTER TABLE ledger_accounts
    DROP CONSTRAINT IF EXISTS ledger_accounts_check;
ALTER TABLE ledger_accounts
    ADD CONSTRAINT ledger_accounts_account_code_check CHECK (account_code IN (
        'CUSTOMER_RECEIVABLE', 'COURIER_AVAILABLE', 'MONO_COMMISSION_REVENUE',
        'VAT_PAYABLE', 'MONO_PAYOUT_CLEARING'));
ALTER TABLE ledger_accounts
    DROP CONSTRAINT IF EXISTS ledger_accounts_owner_account_check;
ALTER TABLE ledger_accounts
    ADD CONSTRAINT ledger_accounts_owner_account_check CHECK (
        (owner_type = 'CUSTOMER' AND account_code = 'CUSTOMER_RECEIVABLE') OR
        (owner_type = 'COURIER' AND account_code = 'COURIER_AVAILABLE') OR
        (owner_type = 'MONO' AND account_code IN (
            'MONO_COMMISSION_REVENUE', 'VAT_PAYABLE', 'MONO_PAYOUT_CLEARING')));

ALTER TABLE ledger_transactions
    DROP CONSTRAINT IF EXISTS ledger_transactions_reference_type_check;
ALTER TABLE ledger_transactions
    DROP CONSTRAINT IF EXISTS ledger_transactions_reference_id_check;
ALTER TABLE ledger_transactions
    ADD CONSTRAINT ledger_transactions_reference_type_check CHECK (
        reference_type IN ('MISSION_EARNING', 'WITHDRAWAL_PAYOUT'));
ALTER TABLE ledger_transactions
    ADD CONSTRAINT ledger_transactions_reference_id_check CHECK (
        (reference_type = 'MISSION_EARNING' AND reference_id ~ '^mis_[0-9a-f]{32}$') OR
        (reference_type = 'WITHDRAWAL_PAYOUT' AND reference_id ~ '^wdr_[0-9a-f]{32}$'));

CREATE TABLE IF NOT EXISTS courier_payout_destinations (
    destination_id text PRIMARY KEY CHECK (destination_id ~ '^cpd_[0-9a-f]{32}$'),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    encrypted_iban text NOT NULL CHECK (encrypted_iban ~ '^v1:[A-Za-z0-9+/=]+:[A-Za-z0-9+/=]+:[A-Za-z0-9+/=]+$'),
    iban_fingerprint text NOT NULL CHECK (iban_fingerprint ~ '^[0-9a-f]{64}$'),
    masked_iban text NOT NULL CHECK (left(masked_iban, 2) = 'IR' AND substring(masked_iban from 3 for 20) = repeat('*', 20) AND right(masked_iban, 4) ~ '^[0-9]{4}$'),
    created_at timestamp with time zone NOT NULL,
    UNIQUE (courier_id, iban_fingerprint)
);

CREATE TABLE IF NOT EXISTS courier_payout_destination_events (
    destination_event_sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    destination_id text NOT NULL REFERENCES courier_payout_destinations(destination_id),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    action text NOT NULL CHECK (action IN ('VERIFIED', 'REVOKED')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    evidence_reference text NOT NULL CHECK (
        length(btrim(evidence_reference)) BETWEEN 1 AND 256 AND
        substring(evidence_reference from 1 for 1) ~ '[A-Za-z0-9]' AND
        evidence_reference !~ '[^A-Za-z0-9._:/~-]'),
    occurred_at timestamp with time zone NOT NULL,
    UNIQUE (actor_id, idempotency_key)
);

CREATE INDEX IF NOT EXISTS courier_payout_destination_current_idx
    ON courier_payout_destination_events (courier_id, destination_event_sequence DESC);

CREATE TABLE IF NOT EXISTS courier_payout_destination_access_audit (
    access_audit_id text PRIMARY KEY CHECK (access_audit_id ~ '^cpa_[0-9a-f]{32}    withdrawal_id text PRIMARY KEY CHECK (withdrawal_id ~ '^wdr_[0-9a-f]{32}$'),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    destination_id text NOT NULL REFERENCES courier_payout_destinations(destination_id),
    amount_toman bigint NOT NULL CHECK (amount_toman > 0),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    created_at timestamp with time zone NOT NULL,
    UNIQUE (courier_id, idempotency_key)
);

CREATE INDEX IF NOT EXISTS courier_withdrawal_requests_history_idx
    ON courier_withdrawal_requests (courier_id, created_at DESC, withdrawal_id DESC);

CREATE TABLE IF NOT EXISTS courier_withdrawal_events (
    withdrawal_event_sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    withdrawal_id text NOT NULL REFERENCES courier_withdrawal_requests(withdrawal_id),
    action text NOT NULL CHECK (action IN ('REQUESTED', 'PROCESSING', 'PAID', 'REJECTED')),
    actor_type text NOT NULL CHECK (actor_type IN ('COURIER', 'OPERATIONS')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    reason_code text NULL CHECK (
        reason_code IS NULL OR
        (length(reason_code) BETWEEN 2 AND 64 AND reason_code = upper(reason_code)
         AND reason_code !~ '[^A-Z0-9_]')),
    bank_reference text NULL CHECK (
        bank_reference IS NULL OR
        (length(btrim(bank_reference)) BETWEEN 1 AND 128
         AND bank_reference !~ '[^A-Za-z0-9._:/~-]')),
    occurred_at timestamp with time zone NOT NULL,
    UNIQUE (withdrawal_id, idempotency_key),
    CHECK ((action = 'PAID' AND actor_type = 'OPERATIONS' AND bank_reference IS NOT NULL) OR
           (action = 'REJECTED' AND actor_type = 'OPERATIONS' AND reason_code IS NOT NULL) OR
           (action IN ('REQUESTED', 'PROCESSING') AND bank_reference IS NULL))
);

CREATE INDEX IF NOT EXISTS courier_withdrawal_events_history_idx
    ON courier_withdrawal_events (withdrawal_id, withdrawal_event_sequence DESC);
CREATE INDEX IF NOT EXISTS courier_withdrawal_events_queue_idx
    ON courier_withdrawal_events (action, withdrawal_event_sequence DESC);

CREATE OR REPLACE FUNCTION reject_courier_finance_history_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME;
END;
$$;

DROP TRIGGER IF EXISTS courier_payout_destinations_immutable ON courier_payout_destinations;
CREATE TRIGGER courier_payout_destinations_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_destinations
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_destinations_no_truncate ON courier_payout_destinations;
CREATE TRIGGER courier_payout_destinations_no_truncate
    BEFORE TRUNCATE ON courier_payout_destinations
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_payout_destination_events_immutable ON courier_payout_destination_events;
CREATE TRIGGER courier_payout_destination_events_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_destination_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_destination_events_no_truncate ON courier_payout_destination_events;
CREATE TRIGGER courier_payout_destination_events_no_truncate
    BEFORE TRUNCATE ON courier_payout_destination_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_payout_destination_access_audit_immutable ON courier_payout_destination_access_audit;
CREATE TRIGGER courier_payout_destination_access_audit_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_destination_access_audit
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_destination_access_audit_no_truncate ON courier_payout_destination_access_audit;
CREATE TRIGGER courier_payout_destination_access_audit_no_truncate
    BEFORE TRUNCATE ON courier_payout_destination_access_audit
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_withdrawal_requests_immutable ON courier_withdrawal_requests;
CREATE TRIGGER courier_withdrawal_requests_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_requests
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_withdrawal_requests_no_truncate ON courier_withdrawal_requests;
CREATE TRIGGER courier_withdrawal_requests_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_requests
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_withdrawal_events_immutable ON courier_withdrawal_events;
CREATE TRIGGER courier_withdrawal_events_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_withdrawal_events_no_truncate ON courier_withdrawal_events;
CREATE TRIGGER courier_withdrawal_events_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    destination_id text NULL REFERENCES courier_payout_destinations(destination_id),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    outcome_code text NOT NULL CHECK (outcome_code IN ('ACCESS_GRANTED', 'NOT_FOUND', 'KEY_UNAVAILABLE', 'DECRYPTION_FAILED')),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS courier_payout_destination_access_audit_history_idx
    ON courier_payout_destination_access_audit (courier_id, occurred_at DESC, access_audit_id DESC);

CREATE TABLE IF NOT EXISTS courier_withdrawal_requests (
    withdrawal_id text PRIMARY KEY CHECK (withdrawal_id ~ '^wdr_[0-9a-f]{32}$'),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    destination_id text NOT NULL REFERENCES courier_payout_destinations(destination_id),
    amount_toman bigint NOT NULL CHECK (amount_toman > 0),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    created_at timestamp with time zone NOT NULL,
    UNIQUE (courier_id, idempotency_key)
);

CREATE INDEX IF NOT EXISTS courier_withdrawal_requests_history_idx
    ON courier_withdrawal_requests (courier_id, created_at DESC, withdrawal_id DESC);

CREATE TABLE IF NOT EXISTS courier_withdrawal_events (
    withdrawal_event_sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    withdrawal_id text NOT NULL REFERENCES courier_withdrawal_requests(withdrawal_id),
    action text NOT NULL CHECK (action IN ('REQUESTED', 'PROCESSING', 'PAID', 'REJECTED')),
    actor_type text NOT NULL CHECK (actor_type IN ('COURIER', 'OPERATIONS')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    reason_code text NULL CHECK (
        reason_code IS NULL OR
        (length(reason_code) BETWEEN 2 AND 64 AND reason_code = upper(reason_code)
         AND reason_code !~ '[^A-Z0-9_]')),
    bank_reference text NULL CHECK (
        bank_reference IS NULL OR
        (length(btrim(bank_reference)) BETWEEN 1 AND 128
         AND bank_reference !~ '[^A-Za-z0-9._:/~-]')),
    occurred_at timestamp with time zone NOT NULL,
    UNIQUE (withdrawal_id, idempotency_key),
    CHECK ((action = 'PAID' AND actor_type = 'OPERATIONS' AND bank_reference IS NOT NULL) OR
           (action = 'REJECTED' AND actor_type = 'OPERATIONS' AND reason_code IS NOT NULL) OR
           (action IN ('REQUESTED', 'PROCESSING') AND bank_reference IS NULL))
);

CREATE INDEX IF NOT EXISTS courier_withdrawal_events_history_idx
    ON courier_withdrawal_events (withdrawal_id, withdrawal_event_sequence DESC);
CREATE INDEX IF NOT EXISTS courier_withdrawal_events_queue_idx
    ON courier_withdrawal_events (action, withdrawal_event_sequence DESC);

CREATE OR REPLACE FUNCTION reject_courier_finance_history_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME;
END;
$$;

DROP TRIGGER IF EXISTS courier_payout_destinations_immutable ON courier_payout_destinations;
CREATE TRIGGER courier_payout_destinations_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_destinations
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_destinations_no_truncate ON courier_payout_destinations;
CREATE TRIGGER courier_payout_destinations_no_truncate
    BEFORE TRUNCATE ON courier_payout_destinations
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_payout_destination_events_immutable ON courier_payout_destination_events;
CREATE TRIGGER courier_payout_destination_events_immutable
    BEFORE UPDATE OR DELETE ON courier_payout_destination_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_payout_destination_events_no_truncate ON courier_payout_destination_events;
CREATE TRIGGER courier_payout_destination_events_no_truncate
    BEFORE TRUNCATE ON courier_payout_destination_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_withdrawal_requests_immutable ON courier_withdrawal_requests;
CREATE TRIGGER courier_withdrawal_requests_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_requests
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_withdrawal_requests_no_truncate ON courier_withdrawal_requests;
CREATE TRIGGER courier_withdrawal_requests_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_requests
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();

DROP TRIGGER IF EXISTS courier_withdrawal_events_immutable ON courier_withdrawal_events;
CREATE TRIGGER courier_withdrawal_events_immutable
    BEFORE UPDATE OR DELETE ON courier_withdrawal_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_finance_history_mutation();
DROP TRIGGER IF EXISTS courier_withdrawal_events_no_truncate ON courier_withdrawal_events;
CREATE TRIGGER courier_withdrawal_events_no_truncate
    BEFORE TRUNCATE ON courier_withdrawal_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_finance_history_mutation();
