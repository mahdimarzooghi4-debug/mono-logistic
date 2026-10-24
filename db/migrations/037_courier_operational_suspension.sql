-- Separate operational suspension from the courier trust lifecycle.
CREATE TABLE IF NOT EXISTS courier_operational_status_events (
    event_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    event_id text PRIMARY KEY CHECK (
        length(event_id) = 36 AND left(event_id, 4) = 'cse_' AND
        substring(event_id from 5) !~ '[^0-9a-f]'),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    action text NOT NULL CHECK (action IN ('SUSPENDED', 'REINSTATED')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    reason_code text NOT NULL CHECK (
        length(reason_code) BETWEEN 2 AND 64 AND
        left(reason_code, 1) ~ '[A-Z]' AND reason_code !~ '[^A-Z0-9_]'),
    occurred_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS courier_operational_status_history_idx
    ON courier_operational_status_events (courier_id, event_sequence DESC);

CREATE OR REPLACE FUNCTION validate_courier_operational_status_event()
RETURNS trigger
LANGUAGE plpgsql
AS $operational_status$
DECLARE
    courier_status text;
    previous_action text;
BEGIN
    SELECT status INTO courier_status
    FROM couriers WHERE courier_id = NEW.courier_id FOR UPDATE;
    IF courier_status IS DISTINCT FROM 'ACTIVE' THEN
        RAISE EXCEPTION 'only ACTIVE couriers may be suspended or reinstated';
    END IF;

    SELECT action INTO previous_action
    FROM courier_operational_status_events
    WHERE courier_id = NEW.courier_id
    ORDER BY event_sequence DESC
    LIMIT 1;

    IF NEW.action = 'SUSPENDED' AND previous_action = 'SUSPENDED' THEN
        RAISE EXCEPTION 'courier is already suspended';
    END IF;
    IF NEW.action = 'REINSTATED' AND previous_action IS DISTINCT FROM 'SUSPENDED' THEN
        RAISE EXCEPTION 'courier is not suspended';
    END IF;
    RETURN NEW;
END;
$operational_status$;

DROP TRIGGER IF EXISTS courier_operational_status_transition_guard ON courier_operational_status_events;
CREATE TRIGGER courier_operational_status_transition_guard
    BEFORE INSERT ON courier_operational_status_events
    FOR EACH ROW EXECUTE FUNCTION validate_courier_operational_status_event();

CREATE TABLE IF NOT EXISTS courier_operational_status_idempotency (
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    operation text NOT NULL CHECK (operation IN ('SUSPEND', 'REINSTATE')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (
        length(request_hash) = 64 AND request_hash !~ '[^0-9a-f]'),
    event_id text NOT NULL UNIQUE REFERENCES courier_operational_status_events(event_id),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (actor_id, operation, idempotency_key)
);

DROP TRIGGER IF EXISTS courier_operational_status_events_immutable ON courier_operational_status_events;
CREATE TRIGGER courier_operational_status_events_immutable
    BEFORE UPDATE OR DELETE ON courier_operational_status_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operational_status_events_no_truncate ON courier_operational_status_events;
CREATE TRIGGER courier_operational_status_events_no_truncate
    BEFORE TRUNCATE ON courier_operational_status_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operational_status_idempotency_immutable ON courier_operational_status_idempotency;
CREATE TRIGGER courier_operational_status_idempotency_immutable
    BEFORE UPDATE OR DELETE ON courier_operational_status_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_operational_status_idempotency_no_truncate ON courier_operational_status_idempotency;
CREATE TRIGGER courier_operational_status_idempotency_no_truncate
    BEFORE TRUNCATE ON courier_operational_status_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
