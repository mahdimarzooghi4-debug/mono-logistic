-- Durable Mono courier registry, ordered trust history, and idempotent operations writes.
CREATE TABLE IF NOT EXISTS couriers (
    courier_id text PRIMARY KEY CHECK (courier_id ~ '^crr_[0-9a-f]{32}$'),
    mode text NOT NULL CHECK (mode ~ '^[A-Z][A-Z0-9_]{1,31}$'),
    status text NOT NULL CHECK (status IN (
        'REGISTERED', 'IDENTITY_VERIFIED', 'DOCUMENTS_SUBMITTED',
        'TRUST_REQUIREMENTS_VERIFIED', 'MONO_APPROVED', 'ACTIVE')),
    preferred_zone_ids jsonb NOT NULL DEFAULT '[]'::jsonb
        CHECK (jsonb_typeof(preferred_zone_ids) = 'array'),
    approved_zone_ids jsonb NOT NULL DEFAULT '[]'::jsonb
        CHECK (jsonb_typeof(approved_zone_ids) = 'array'),
    primary_zone_id text NULL,
    created_by text NOT NULL CHECK (length(btrim(created_by)) BETWEEN 1 AND 128),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CHECK (primary_zone_id IS NULL OR primary_zone_id <> ''),
    CHECK (status <> 'ACTIVE' OR
        (jsonb_array_length(approved_zone_ids) > 0 AND primary_zone_id IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS couriers_status_created_idx
    ON couriers (status, created_at DESC, courier_id DESC);

CREATE TABLE IF NOT EXISTS courier_trust_transitions (
    transition_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    transition_id text PRIMARY KEY CHECK (transition_id ~ '^ctt_[0-9a-f]{32}
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    from_status text NOT NULL CHECK (from_status IN (
        'REGISTERED', 'IDENTITY_VERIFIED', 'DOCUMENTS_SUBMITTED',
        'TRUST_REQUIREMENTS_VERIFIED', 'MONO_APPROVED')),
    to_status text NOT NULL CHECK (to_status IN (
        'IDENTITY_VERIFIED', 'DOCUMENTS_SUBMITTED', 'TRUST_REQUIREMENTS_VERIFIED',
        'MONO_APPROVED', 'ACTIVE')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    reason_code text NOT NULL CHECK (reason_code ~ '^[A-Z][A-Z0-9_]{1,63}$'),
    evidence_reference text NOT NULL CHECK (
        evidence_reference ~ '^[A-Za-z0-9][A-Za-z0-9._:/~-]{0,255}$'),
    occurred_at timestamp with time zone NOT NULL,
    UNIQUE (courier_id, to_status)
);

CREATE INDEX IF NOT EXISTS courier_trust_transitions_history_idx
    ON courier_trust_transitions (courier_id, occurred_at, transition_id);


CREATE OR REPLACE FUNCTION validate_courier_trust_status_step()
RETURNS trigger
LANGUAGE plpgsql
AS $
DECLARE
    expected_status text;
BEGIN
    IF NEW.status IS NOT DISTINCT FROM OLD.status THEN
        RETURN NEW;
    END IF;

    expected_status := CASE OLD.status
        WHEN 'REGISTERED' THEN 'IDENTITY_VERIFIED'
        WHEN 'IDENTITY_VERIFIED' THEN 'DOCUMENTS_SUBMITTED'
        WHEN 'DOCUMENTS_SUBMITTED' THEN 'TRUST_REQUIREMENTS_VERIFIED'
        WHEN 'TRUST_REQUIREMENTS_VERIFIED' THEN 'MONO_APPROVED'
        WHEN 'MONO_APPROVED' THEN 'ACTIVE'
        ELSE NULL
    END;
    IF expected_status IS NULL OR NEW.status IS DISTINCT FROM expected_status THEN
        RAISE EXCEPTION 'courier trust status must advance by exactly one lifecycle step';
    END IF;
    RETURN NEW;
END;
$;

DROP TRIGGER IF EXISTS couriers_trust_status_step ON couriers;
CREATE TRIGGER couriers_trust_status_step
    BEFORE UPDATE OF status ON couriers
    FOR EACH ROW EXECUTE FUNCTION validate_courier_trust_status_step();

CREATE OR REPLACE FUNCTION require_courier_trust_transition_record()
RETURNS trigger
LANGUAGE plpgsql
AS $
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM courier_trust_transitions
        WHERE courier_id = NEW.courier_id
          AND from_status = OLD.status
          AND to_status = NEW.status) THEN
        RAISE EXCEPTION 'courier status change requires a matching trust transition record';
    END IF;
    RETURN NULL;
END;
$;

DROP TRIGGER IF EXISTS couriers_status_transition_record_required ON couriers;
CREATE CONSTRAINT TRIGGER couriers_status_transition_record_required
    AFTER UPDATE OF status ON couriers
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION require_courier_trust_transition_record();

CREATE TABLE IF NOT EXISTS courier_operations_idempotency (
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    operation text NOT NULL CHECK (operation IN ('REGISTER', 'TRUST_TRANSITION')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    response jsonb NOT NULL CHECK (jsonb_typeof(response) = 'object'),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (actor_id, operation, idempotency_key)
);

CREATE OR REPLACE FUNCTION reject_courier_append_only_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME;
END;
$$;

DROP TRIGGER IF EXISTS courier_trust_transitions_immutable ON courier_trust_transitions;
CREATE TRIGGER courier_trust_transitions_immutable
    BEFORE UPDATE OR DELETE ON courier_trust_transitions
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_trust_transitions_no_truncate ON courier_trust_transitions;
CREATE TRIGGER courier_trust_transitions_no_truncate
    BEFORE TRUNCATE ON courier_trust_transitions
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operations_idempotency_immutable ON courier_operations_idempotency;
CREATE TRIGGER courier_operations_idempotency_immutable
    BEFORE UPDATE OR DELETE ON courier_operations_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operations_idempotency_no_truncate ON courier_operations_idempotency;
CREATE TRIGGER courier_operations_idempotency_no_truncate
    BEFORE TRUNCATE ON courier_operations_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    from_status text NOT NULL CHECK (from_status IN (
        'REGISTERED', 'IDENTITY_VERIFIED', 'DOCUMENTS_SUBMITTED',
        'TRUST_REQUIREMENTS_VERIFIED', 'MONO_APPROVED')),
    to_status text NOT NULL CHECK (to_status IN (
        'IDENTITY_VERIFIED', 'DOCUMENTS_SUBMITTED', 'TRUST_REQUIREMENTS_VERIFIED',
        'MONO_APPROVED', 'ACTIVE')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    reason_code text NOT NULL CHECK (reason_code ~ '^[A-Z][A-Z0-9_]{1,63}$'),
    evidence_reference text NOT NULL CHECK (
        evidence_reference ~ '^[A-Za-z0-9][A-Za-z0-9._:/~-]{0,255}$'),
    occurred_at timestamp with time zone NOT NULL,
    UNIQUE (courier_id, to_status)
);

CREATE INDEX IF NOT EXISTS courier_trust_transitions_history_idx
    ON courier_trust_transitions (courier_id, occurred_at, transition_id);

CREATE TABLE IF NOT EXISTS courier_operations_idempotency (
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    operation text NOT NULL CHECK (operation IN ('REGISTER', 'TRUST_TRANSITION')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    response jsonb NOT NULL CHECK (jsonb_typeof(response) = 'object'),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (actor_id, operation, idempotency_key)
);

CREATE OR REPLACE FUNCTION reject_courier_append_only_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME;
END;
$$;

DROP TRIGGER IF EXISTS courier_trust_transitions_immutable ON courier_trust_transitions;
CREATE TRIGGER courier_trust_transitions_immutable
    BEFORE UPDATE OR DELETE ON courier_trust_transitions
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_trust_transitions_no_truncate ON courier_trust_transitions;
CREATE TRIGGER courier_trust_transitions_no_truncate
    BEFORE TRUNCATE ON courier_trust_transitions
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operations_idempotency_immutable ON courier_operations_idempotency;
CREATE TRIGGER courier_operations_idempotency_immutable
    BEFORE UPDATE OR DELETE ON courier_operations_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operations_idempotency_no_truncate ON courier_operations_idempotency;
CREATE TRIGGER courier_operations_idempotency_no_truncate
    BEFORE TRUNCATE ON courier_operations_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
