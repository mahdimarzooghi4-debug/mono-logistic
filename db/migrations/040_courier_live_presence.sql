-- Live courier presence is an operational self-service signal, separate from planned duty windows.
CREATE TABLE IF NOT EXISTS courier_live_presence (
    courier_id text PRIMARY KEY REFERENCES couriers(courier_id),
    is_online boolean NOT NULL,
    zone_id text NULL CHECK (zone_id IS NULL OR length(btrim(zone_id)) BETWEEN 1 AND 128),
    changed_at timestamp with time zone NOT NULL,
    event_id text NOT NULL,
    CHECK ((is_online AND zone_id IS NOT NULL) OR (NOT is_online AND zone_id IS NULL))
);

CREATE TABLE IF NOT EXISTS courier_live_presence_events (
    event_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    event_id text PRIMARY KEY CHECK (
        length(event_id) = 36 AND left(event_id, 4) = 'clp_' AND
        substring(event_id from 5) !~ '[^0-9a-f]'),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    action text NOT NULL CHECK (action IN ('ONLINE', 'OFFLINE')),
    zone_id text NOT NULL CHECK (length(btrim(zone_id)) BETWEEN 1 AND 128),
    occurred_at timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS courier_live_presence_events_history_idx
    ON courier_live_presence_events (courier_id, event_sequence DESC);

CREATE TABLE IF NOT EXISTS courier_live_presence_idempotency (
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    operation text NOT NULL CHECK (operation IN ('ONLINE', 'OFFLINE')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (length(request_hash) = 64 AND request_hash !~ '[^0-9a-f]'),
    response jsonb NOT NULL CHECK (jsonb_typeof(response) = 'object'),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (courier_id, operation, idempotency_key)
);

CREATE OR REPLACE FUNCTION protect_courier_live_presence_projection()
RETURNS trigger
LANGUAGE plpgsql
AS $live_presence_projection_guard$
BEGIN
    IF pg_trigger_depth() < 2 THEN
        RAISE EXCEPTION 'courier live presence projection is maintained by events';
    END IF;
    RETURN NEW;
END;
$live_presence_projection_guard$;

DROP TRIGGER IF EXISTS courier_live_presence_event_only ON courier_live_presence;
CREATE TRIGGER courier_live_presence_event_only
    BEFORE INSERT OR UPDATE OR DELETE ON courier_live_presence
    FOR EACH ROW EXECUTE FUNCTION protect_courier_live_presence_projection();

DROP TRIGGER IF EXISTS courier_live_presence_no_truncate ON courier_live_presence;
CREATE TRIGGER courier_live_presence_no_truncate
    BEFORE TRUNCATE ON courier_live_presence
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

CREATE OR REPLACE FUNCTION validate_courier_live_presence_event()
RETURNS trigger
LANGUAGE plpgsql
AS $live_presence_event_guard$
DECLARE
    current_status text;
    approved_zones jsonb;
    suspended boolean;
    is_currently_online boolean;
BEGIN
    SELECT status, approved_zone_ids INTO current_status, approved_zones
    FROM couriers WHERE courier_id = NEW.courier_id FOR UPDATE;
    IF current_status IS NULL THEN
        RAISE EXCEPTION 'courier does not exist';
    END IF;
    SELECT COALESCE(latest.action = 'SUSPENDED', FALSE) INTO suspended
    FROM couriers courier
    LEFT JOIN LATERAL (
        SELECT action FROM courier_operational_status_events
        WHERE courier_id = courier.courier_id ORDER BY event_sequence DESC LIMIT 1
    ) latest ON TRUE
    WHERE courier.courier_id = NEW.courier_id;
    IF current_status <> 'ACTIVE' OR suspended THEN
        RAISE EXCEPTION 'only active unsuspended couriers may change live presence';
    END IF;
    SELECT COALESCE(is_online, FALSE) INTO is_currently_online
    FROM courier_live_presence WHERE courier_id = NEW.courier_id;
    is_currently_online := COALESCE(is_currently_online, FALSE);
    IF NEW.action = 'ONLINE' THEN
        IF is_currently_online THEN
            RAISE EXCEPTION 'courier is already online';
        END IF;
        IF NOT (approved_zones ? NEW.zone_id) OR
           NOT EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = NEW.zone_id) THEN
            RAISE EXCEPTION 'courier zone is not approved and active';
        END IF;
    ELSE
        IF NOT is_currently_online THEN
            RAISE EXCEPTION 'courier is already offline';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM courier_live_presence
            WHERE courier_id = NEW.courier_id AND zone_id = NEW.zone_id
        ) THEN
            RAISE EXCEPTION 'offline event zone does not match current presence';
        END IF;
    END IF;
    RETURN NEW;
END;
$live_presence_event_guard$;

CREATE OR REPLACE FUNCTION apply_courier_live_presence_event()
RETURNS trigger
LANGUAGE plpgsql
AS $apply_live_presence$
BEGIN
    INSERT INTO courier_live_presence (courier_id, is_online, zone_id, changed_at, event_id)
    VALUES (NEW.courier_id, NEW.action = 'ONLINE',
        CASE WHEN NEW.action = 'ONLINE' THEN NEW.zone_id ELSE NULL END,
        NEW.occurred_at, NEW.event_id)
    ON CONFLICT (courier_id) DO UPDATE SET
        is_online = EXCLUDED.is_online,
        zone_id = EXCLUDED.zone_id,
        changed_at = EXCLUDED.changed_at,
        event_id = EXCLUDED.event_id;
    RETURN NULL;
END;
$apply_live_presence$;

DROP TRIGGER IF EXISTS courier_live_presence_event_guard ON courier_live_presence_events;
CREATE TRIGGER courier_live_presence_event_guard
    BEFORE INSERT ON courier_live_presence_events
    FOR EACH ROW EXECUTE FUNCTION validate_courier_live_presence_event();

DROP TRIGGER IF EXISTS courier_live_presence_event_apply ON courier_live_presence_events;
CREATE TRIGGER courier_live_presence_event_apply
    AFTER INSERT ON courier_live_presence_events
    FOR EACH ROW EXECUTE FUNCTION apply_courier_live_presence_event();

DROP TRIGGER IF EXISTS courier_live_presence_events_immutable ON courier_live_presence_events;
CREATE TRIGGER courier_live_presence_events_immutable
    BEFORE UPDATE OR DELETE ON courier_live_presence_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_live_presence_events_no_truncate ON courier_live_presence_events;
CREATE TRIGGER courier_live_presence_events_no_truncate
    BEFORE TRUNCATE ON courier_live_presence_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_live_presence_idempotency_immutable ON courier_live_presence_idempotency;
CREATE TRIGGER courier_live_presence_idempotency_immutable
    BEFORE UPDATE OR DELETE ON courier_live_presence_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();

DROP TRIGGER IF EXISTS courier_live_presence_idempotency_no_truncate ON courier_live_presence_idempotency;
CREATE TRIGGER courier_live_presence_idempotency_no_truncate
    BEFORE TRUNCATE ON courier_live_presence_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
