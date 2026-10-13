-- Mono-owned binding between a trusted OIDC subject and a courier registry record.
CREATE TABLE IF NOT EXISTS courier_identity_bindings (
    courier_id text PRIMARY KEY REFERENCES couriers(courier_id),
    issuer text NOT NULL CHECK (length(issuer) BETWEEN 1 AND 512),
    subject text NOT NULL CHECK (length(subject) BETWEEN 1 AND 512),
    status text NOT NULL CHECK (status IN ('BOUND', 'REVOKED')),
    updated_at timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS courier_identity_bindings_active_subject_idx
    ON courier_identity_bindings (issuer, subject) WHERE status = 'BOUND';

CREATE OR REPLACE FUNCTION require_courier_identity_binding_event_projection()
RETURNS trigger
LANGUAGE plpgsql
AS $projection_guard$
BEGIN
    IF pg_trigger_depth() < 2 THEN
        RAISE EXCEPTION 'courier identity binding projection is maintained by events';
    END IF;
    RETURN NEW;
END;
$projection_guard$;

DROP TRIGGER IF EXISTS courier_identity_bindings_event_only ON courier_identity_bindings;
CREATE TRIGGER courier_identity_bindings_event_only
    BEFORE INSERT OR UPDATE OR DELETE ON courier_identity_bindings
    FOR EACH ROW EXECUTE FUNCTION require_courier_identity_binding_event_projection();

DROP TRIGGER IF EXISTS courier_identity_bindings_no_truncate ON courier_identity_bindings;
CREATE TRIGGER courier_identity_bindings_no_truncate
    BEFORE TRUNCATE ON courier_identity_bindings
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

CREATE TABLE IF NOT EXISTS courier_identity_binding_events (
    event_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    event_id text PRIMARY KEY CHECK (
        length(event_id) = 37 AND left(event_id, 5) = 'cibe_' AND
        substring(event_id from 6) !~ '[^0-9a-f]'),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    action text NOT NULL CHECK (action IN ('BOUND', 'REVOKED')),
    issuer text NOT NULL CHECK (length(issuer) BETWEEN 1 AND 512),
    subject text NOT NULL CHECK (length(subject) BETWEEN 1 AND 512),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    reason_code text NOT NULL CHECK (
        length(reason_code) BETWEEN 2 AND 64 AND left(reason_code, 1) ~ '[A-Z]' AND reason_code !~ '[^A-Z0-9_]'),
    evidence_reference text NOT NULL CHECK (
        length(evidence_reference) BETWEEN 1 AND 256 AND
        substring(evidence_reference from 1 for 1) ~ '[A-Za-z0-9]' AND
        evidence_reference !~ '[^A-Za-z0-9._:/~-]'),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS courier_identity_binding_events_history_idx
    ON courier_identity_binding_events (courier_id, event_sequence DESC);

CREATE OR REPLACE FUNCTION validate_courier_identity_binding_event()
RETURNS trigger
LANGUAGE plpgsql
AS $identity_binding$
DECLARE
    courier_status text;
    previous_status text;
    suspended boolean;
BEGIN
    SELECT status INTO courier_status
    FROM couriers WHERE courier_id = NEW.courier_id FOR UPDATE;
    IF courier_status IS NULL THEN
        RAISE EXCEPTION 'courier does not exist';
    END IF;

    SELECT status INTO previous_status
    FROM courier_identity_bindings
    WHERE courier_id = NEW.courier_id;

    IF NEW.action = 'BOUND' THEN
        SELECT COALESCE(latest.action = 'SUSPENDED', FALSE) INTO suspended
        FROM couriers courier
        LEFT JOIN LATERAL (
            SELECT action FROM courier_operational_status_events
            WHERE courier_id = courier.courier_id ORDER BY event_sequence DESC LIMIT 1
        ) latest ON TRUE
        WHERE courier.courier_id = NEW.courier_id;
        IF courier_status <> 'ACTIVE' OR suspended THEN
            RAISE EXCEPTION 'only active unsuspended couriers may be bound';
        END IF;
        IF previous_status = 'BOUND' THEN
            RAISE EXCEPTION 'courier already has an active identity binding';
        END IF;
        IF EXISTS (
            SELECT 1 FROM courier_identity_bindings
            WHERE issuer = NEW.issuer AND subject = NEW.subject AND status = 'BOUND'
              AND courier_id <> NEW.courier_id
        ) THEN
            RAISE EXCEPTION 'identity subject is already bound';
        END IF;
    ELSE
        IF previous_status IS DISTINCT FROM 'BOUND' THEN
            RAISE EXCEPTION 'courier has no active identity binding to revoke';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM courier_identity_bindings
            WHERE courier_id = NEW.courier_id AND issuer = NEW.issuer AND subject = NEW.subject
        ) THEN
            RAISE EXCEPTION 'revocation identity does not match active binding';
        END IF;
    END IF;
    RETURN NEW;
END;
$identity_binding$;

CREATE OR REPLACE FUNCTION apply_courier_identity_binding_event()
RETURNS trigger
LANGUAGE plpgsql
AS $apply_identity_binding$
BEGIN
    INSERT INTO courier_identity_bindings (courier_id, issuer, subject, status, updated_at)
    VALUES (NEW.courier_id, NEW.issuer, NEW.subject, NEW.action, NEW.occurred_at)
    ON CONFLICT (courier_id) DO UPDATE SET
        issuer = EXCLUDED.issuer,
        subject = EXCLUDED.subject,
        status = EXCLUDED.status,
        updated_at = EXCLUDED.updated_at;
    RETURN NULL;
END;
$apply_identity_binding$;

DROP TRIGGER IF EXISTS courier_identity_binding_event_guard ON courier_identity_binding_events;
CREATE TRIGGER courier_identity_binding_event_guard
    BEFORE INSERT ON courier_identity_binding_events
    FOR EACH ROW EXECUTE FUNCTION validate_courier_identity_binding_event();

DROP TRIGGER IF EXISTS courier_identity_binding_event_projection ON courier_identity_binding_events;
CREATE TRIGGER courier_identity_binding_event_projection
    AFTER INSERT ON courier_identity_binding_events
    FOR EACH ROW EXECUTE FUNCTION apply_courier_identity_binding_event();

CREATE TABLE IF NOT EXISTS courier_identity_binding_idempotency (
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    operation text NOT NULL CHECK (operation IN ('BIND', 'REVOKE')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (length(request_hash) = 64 AND request_hash !~ '[^0-9a-f]'),
    event_id text NOT NULL UNIQUE REFERENCES courier_identity_binding_events(event_id),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (actor_id, operation, idempotency_key)
);

DROP TRIGGER IF EXISTS courier_identity_binding_events_immutable ON courier_identity_binding_events;
CREATE TRIGGER courier_identity_binding_events_immutable
    BEFORE UPDATE OR DELETE ON courier_identity_binding_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS courier_identity_binding_events_no_truncate ON courier_identity_binding_events;
CREATE TRIGGER courier_identity_binding_events_no_truncate
    BEFORE TRUNCATE ON courier_identity_binding_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS courier_identity_binding_idempotency_immutable ON courier_identity_binding_idempotency;
CREATE TRIGGER courier_identity_binding_idempotency_immutable
    BEFORE UPDATE OR DELETE ON courier_identity_binding_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS courier_identity_binding_idempotency_no_truncate ON courier_identity_binding_idempotency;
CREATE TRIGGER courier_identity_binding_idempotency_no_truncate
    BEFORE TRUNCATE ON courier_identity_binding_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
