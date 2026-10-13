-- One-at-a-time courier mission offers with courier consent and atomic Mission assignment.
CREATE TABLE IF NOT EXISTS mission_courier_offers (
    offer_id text PRIMARY KEY CHECK (
        length(offer_id) = 36 AND left(offer_id, 4) = 'mco_' AND
        substring(offer_id from 5) !~ '[^0-9a-f]'),
    mission_id text NOT NULL REFERENCES missions(mission_id),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    zone_id text NOT NULL CHECK (length(btrim(zone_id)) BETWEEN 1 AND 128),
    status text NOT NULL CHECK (status IN ('OFFERED', 'ACCEPTED', 'DECLINED', 'CANCELLED', 'COMPLETED')),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    last_event_id text NOT NULL,
    CHECK (updated_at >= created_at)
);
CREATE UNIQUE INDEX IF NOT EXISTS mission_courier_offers_one_active_mission_idx
    ON mission_courier_offers (mission_id)
    WHERE status IN ('OFFERED', 'ACCEPTED');
CREATE UNIQUE INDEX IF NOT EXISTS mission_courier_offers_one_active_courier_idx
    ON mission_courier_offers (courier_id)
    WHERE status IN ('OFFERED', 'ACCEPTED');
CREATE INDEX IF NOT EXISTS mission_courier_offers_courier_inbox_idx
    ON mission_courier_offers (courier_id, created_at DESC, offer_id DESC)
    WHERE status = 'OFFERED';

CREATE TABLE IF NOT EXISTS mission_courier_offer_events (
    event_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    event_id text PRIMARY KEY CHECK (
        length(event_id) = 36 AND left(event_id, 4) = 'mce_' AND
        substring(event_id from 5) !~ '[^0-9a-f]'),
    offer_id text NOT NULL CHECK (
        length(offer_id) = 36 AND left(offer_id, 4) = 'mco_' AND
        substring(offer_id from 5) !~ '[^0-9a-f]'),
    mission_id text NOT NULL REFERENCES missions(mission_id),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    zone_id text NOT NULL CHECK (length(btrim(zone_id)) BETWEEN 1 AND 128),
    action text NOT NULL CHECK (action IN ('OFFERED', 'ACCEPTED', 'DECLINED', 'CANCELLED', 'COMPLETED')),
    actor_type text NOT NULL CHECK (actor_type IN ('OPERATIONS', 'COURIER', 'SYSTEM')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 256),
    occurred_at timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS mission_courier_offer_events_history_idx
    ON mission_courier_offer_events (offer_id, event_sequence DESC);

CREATE TABLE IF NOT EXISTS mission_courier_offer_idempotency (
    scope_id text NOT NULL CHECK (length(btrim(scope_id)) BETWEEN 1 AND 128),
    operation text NOT NULL CHECK (operation IN ('CREATE_OFFER', 'ACCEPT_OFFER', 'DECLINE_OFFER')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (length(request_hash) = 64 AND request_hash !~ '[^0-9a-f]'),
    response jsonb NOT NULL CHECK (jsonb_typeof(response) = 'object'),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (scope_id, operation, idempotency_key)
);

CREATE OR REPLACE FUNCTION protect_mission_courier_offer_projection()
RETURNS trigger
LANGUAGE plpgsql
AS $offer_projection_guard$
BEGIN
    IF pg_trigger_depth() < 2 THEN
        RAISE EXCEPTION 'mission courier offer projection is maintained by events';
    END IF;
    RETURN NEW;
END;
$offer_projection_guard$;

DROP TRIGGER IF EXISTS mission_courier_offers_event_only ON mission_courier_offers;
CREATE TRIGGER mission_courier_offers_event_only
    BEFORE INSERT OR UPDATE OR DELETE ON mission_courier_offers
    FOR EACH ROW EXECUTE FUNCTION protect_mission_courier_offer_projection();
DROP TRIGGER IF EXISTS mission_courier_offers_no_truncate ON mission_courier_offers;
CREATE TRIGGER mission_courier_offers_no_truncate
    BEFORE TRUNCATE ON mission_courier_offers
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

CREATE OR REPLACE FUNCTION validate_mission_courier_offer_event()
RETURNS trigger
LANGUAGE plpgsql
AS $offer_event_guard$
DECLARE
    mission_status text;
    courier_status text;
    approved_zones jsonb;
    online boolean;
    presence_zone text;
    suspended boolean;
    current_offer_status text;
    offer_mission_id text;
    offer_courier_id text;
    offer_zone_id text;
    offer_customer_id text;
BEGIN
    IF NEW.action = 'OFFERED' THEN
        offer_mission_id := NEW.mission_id;
        offer_customer_id := NEW.customer_id;
        offer_courier_id := NEW.courier_id;
        offer_zone_id := NEW.zone_id;
    ELSE
        SELECT offer.status, offer.mission_id, offer.customer_id, offer.courier_id, offer.zone_id
            INTO current_offer_status, offer_mission_id, offer_customer_id, offer_courier_id, offer_zone_id
        FROM mission_courier_offers offer WHERE offer.offer_id = NEW.offer_id FOR UPDATE;
    END IF;
    IF offer_mission_id IS NULL OR offer_courier_id IS NULL THEN
        RAISE EXCEPTION 'mission courier offer does not exist';
    END IF;

    IF NEW.action = 'OFFERED' THEN
        IF NEW.actor_type <> 'OPERATIONS' THEN
            RAISE EXCEPTION 'only operations may create a courier offer';
        END IF;
        SELECT status, customer_id INTO mission_status, offer_customer_id FROM missions
        WHERE mission_id = offer_mission_id FOR UPDATE;
        IF mission_status <> 'SEARCHING_COURIER' OR
           offer_customer_id IS DISTINCT FROM NEW.customer_id THEN
            RAISE EXCEPTION 'mission is not searching for a courier';
        END IF;
    ELSE
        IF current_offer_status IS DISTINCT FROM 'OFFERED' AND
            NOT (NEW.action IN ('CANCELLED', 'COMPLETED') AND current_offer_status = 'ACCEPTED') THEN
            RAISE EXCEPTION 'mission courier offer transition is invalid';
        END IF;
        IF NEW.action IN ('ACCEPTED', 'DECLINED') AND NEW.actor_type <> 'COURIER' THEN
            RAISE EXCEPTION 'only the offered courier may respond';
        END IF;
        IF NEW.action IN ('CANCELLED', 'COMPLETED') AND NEW.actor_type <> 'SYSTEM' THEN
            RAISE EXCEPTION 'system transition actor is required';
        END IF;
        IF NEW.action = 'COMPLETED' AND current_offer_status <> 'ACCEPTED' THEN
            RAISE EXCEPTION 'only an accepted courier offer can complete';
        END IF;
        SELECT status INTO mission_status FROM missions
        WHERE mission_id = offer_mission_id FOR UPDATE;
        IF NEW.mission_id IS DISTINCT FROM offer_mission_id OR
           NEW.customer_id IS DISTINCT FROM offer_customer_id OR
           NEW.courier_id IS DISTINCT FROM offer_courier_id OR
           NEW.zone_id IS DISTINCT FROM offer_zone_id THEN
            RAISE EXCEPTION 'mission offer event identity does not match its offer';
        END IF;
        IF NEW.action = 'ACCEPTED' AND mission_status <> 'SEARCHING_COURIER' THEN
            RAISE EXCEPTION 'mission is not searching for a courier';
        END IF;
        IF NEW.action = 'CANCELLED' AND mission_status NOT IN ('CANCELLED', 'FAILED') THEN
            RAISE EXCEPTION 'mission is not cancelled or failed';
        END IF;
        IF NEW.action = 'COMPLETED' AND mission_status <> 'DELIVERED' THEN
            RAISE EXCEPTION 'mission is not delivered';
        END IF;
        IF NEW.action IN ('ACCEPTED', 'DECLINED') AND NEW.actor_id <> offer_courier_id THEN
            RAISE EXCEPTION 'courier does not own this offer';
        END IF;
    END IF;

    IF NEW.action IN ('OFFERED', 'ACCEPTED') THEN
        SELECT courier.status, courier.approved_zone_ids, COALESCE(latest.action = 'SUSPENDED', FALSE)
            INTO courier_status, approved_zones, suspended
        FROM couriers courier
        LEFT JOIN LATERAL (
            SELECT action FROM courier_operational_status_events
            WHERE courier_id = courier.courier_id ORDER BY event_sequence DESC LIMIT 1
        ) latest ON TRUE
        WHERE courier.courier_id = offer_courier_id FOR UPDATE OF courier;
        SELECT COALESCE(presence.is_online, FALSE), presence.zone_id
            INTO online, presence_zone
        FROM courier_live_presence presence WHERE presence.courier_id = offer_courier_id;
        IF courier_status <> 'ACTIVE' OR suspended OR NOT COALESCE(online, FALSE) OR
           presence_zone IS DISTINCT FROM offer_zone_id OR NOT (approved_zones ? offer_zone_id) OR
           NOT EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = offer_zone_id) THEN
            RAISE EXCEPTION 'courier must be active, unsuspended, online in an approved active zone';
        END IF;
    END IF;
    RETURN NEW;
END;
$offer_event_guard$;

CREATE OR REPLACE FUNCTION apply_mission_courier_offer_event()
RETURNS trigger
LANGUAGE plpgsql
AS $apply_offer_event$
BEGIN
    IF NEW.action = 'OFFERED' THEN
        INSERT INTO mission_courier_offers
            (offer_id, mission_id, customer_id, courier_id, zone_id, status, created_at, updated_at, last_event_id)
        VALUES (NEW.offer_id, NEW.mission_id, NEW.customer_id, NEW.courier_id,
            NEW.zone_id, 'OFFERED', NEW.occurred_at, NEW.occurred_at, NEW.event_id);
    ELSE
        UPDATE mission_courier_offers
        SET status = NEW.action, updated_at = NEW.occurred_at, last_event_id = NEW.event_id
        WHERE offer_id = NEW.offer_id;
    END IF;
    RETURN NULL;
END;
$apply_offer_event$;

DROP TRIGGER IF EXISTS mission_courier_offer_event_guard ON mission_courier_offer_events;
CREATE TRIGGER mission_courier_offer_event_guard
    BEFORE INSERT ON mission_courier_offer_events
    FOR EACH ROW EXECUTE FUNCTION validate_mission_courier_offer_event();
DROP TRIGGER IF EXISTS mission_courier_offer_event_apply ON mission_courier_offer_events;
CREATE TRIGGER mission_courier_offer_event_apply
    AFTER INSERT ON mission_courier_offer_events
    FOR EACH ROW EXECUTE FUNCTION apply_mission_courier_offer_event();
DROP TRIGGER IF EXISTS mission_courier_offer_events_immutable ON mission_courier_offer_events;
CREATE TRIGGER mission_courier_offer_events_immutable
    BEFORE UPDATE OR DELETE ON mission_courier_offer_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS mission_courier_offer_events_no_truncate ON mission_courier_offer_events;
CREATE TRIGGER mission_courier_offer_events_no_truncate
    BEFORE TRUNCATE ON mission_courier_offer_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS mission_courier_offer_idempotency_immutable ON mission_courier_offer_idempotency;
CREATE TRIGGER mission_courier_offer_idempotency_immutable
    BEFORE UPDATE OR DELETE ON mission_courier_offer_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS mission_courier_offer_idempotency_no_truncate ON mission_courier_offer_idempotency;
CREATE TRIGGER mission_courier_offer_idempotency_no_truncate
    BEFORE TRUNCATE ON mission_courier_offer_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

CREATE OR REPLACE FUNCTION close_mission_courier_offer_on_mission_terminal()
RETURNS trigger
LANGUAGE plpgsql
AS $close_mission_offer$
DECLARE
    active_offer record;
BEGIN
    IF NEW.status = OLD.status OR NEW.status NOT IN ('CANCELLED', 'FAILED', 'DELIVERED') THEN
        RETURN NULL;
    END IF;
    FOR active_offer IN
        SELECT offer_id, mission_id, customer_id, courier_id, zone_id, status
        FROM mission_courier_offers
        WHERE mission_id = NEW.mission_id AND status IN ('OFFERED', 'ACCEPTED')
        FOR UPDATE
    LOOP
        INSERT INTO mission_courier_offer_events
            (event_id, offer_id, mission_id, customer_id, courier_id, zone_id,
                action, actor_type, actor_id, reason, occurred_at)
        VALUES (
            'mce_' || replace(gen_random_uuid()::text, '-', ''),
            active_offer.offer_id, active_offer.mission_id, active_offer.customer_id,
            active_offer.courier_id, active_offer.zone_id,
            CASE WHEN NEW.status = 'DELIVERED' THEN 'COMPLETED' ELSE 'CANCELLED' END,
            'SYSTEM', 'mono-mission-lifecycle', 'MISSION_' || NEW.status, now());
    END LOOP;
    RETURN NULL;
END;
$close_mission_offer$;

DROP TRIGGER IF EXISTS missions_close_courier_offers ON missions;
CREATE TRIGGER missions_close_courier_offers
    AFTER UPDATE OF status ON missions
    FOR EACH ROW EXECUTE FUNCTION close_mission_courier_offer_on_mission_terminal();
