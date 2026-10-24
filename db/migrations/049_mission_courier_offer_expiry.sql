-- Add timed offer expiry while keeping offer history append-only.
ALTER TABLE mission_courier_offers ADD COLUMN IF NOT EXISTS expires_at timestamp with time zone NULL;
ALTER TABLE mission_courier_offer_events ADD COLUMN IF NOT EXISTS expires_at timestamp with time zone NULL;

ALTER TABLE mission_courier_offers DROP CONSTRAINT IF EXISTS mission_courier_offers_status_check;
ALTER TABLE mission_courier_offers ADD CONSTRAINT mission_courier_offers_status_check
    CHECK (status IN ('OFFERED', 'ACCEPTED', 'DECLINED', 'CANCELLED', 'COMPLETED', 'WITHDRAWN', 'RELEASED', 'EXPIRED'));
ALTER TABLE mission_courier_offer_events DROP CONSTRAINT IF EXISTS mission_courier_offer_events_action_check;
ALTER TABLE mission_courier_offer_events ADD CONSTRAINT mission_courier_offer_events_action_check
    CHECK (action IN ('OFFERED', 'ACCEPTED', 'DECLINED', 'CANCELLED', 'COMPLETED', 'WITHDRAWN', 'RELEASED', 'EXPIRED'));
ALTER TABLE mission_courier_offer_idempotency DROP CONSTRAINT IF EXISTS mission_courier_offer_idempotency_operation_check;
ALTER TABLE mission_courier_offer_idempotency ADD CONSTRAINT mission_courier_offer_idempotency_operation_check
    CHECK (operation IN ('CREATE_OFFER', 'ACCEPT_OFFER', 'DECLINE_OFFER', 'WITHDRAW_OFFER', 'RELEASE_ASSIGNMENT', 'EXPIRE_OFFER'));

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
    offer_expires_at timestamp with time zone;
BEGIN
    IF NEW.action = 'OFFERED' THEN
        offer_mission_id := NEW.mission_id;
        offer_customer_id := NEW.customer_id;
        offer_courier_id := NEW.courier_id;
        offer_zone_id := NEW.zone_id;
    ELSE
        SELECT offer.status, offer.mission_id, offer.customer_id, offer.courier_id, offer.zone_id, offer.expires_at
            INTO current_offer_status, offer_mission_id, offer_customer_id, offer_courier_id, offer_zone_id, offer_expires_at
        FROM mission_courier_offers offer WHERE offer.offer_id = NEW.offer_id FOR UPDATE;
    END IF;
    IF offer_mission_id IS NULL OR offer_courier_id IS NULL THEN
        RAISE EXCEPTION 'mission courier offer does not exist';
    END IF;

    IF NEW.action = 'OFFERED' THEN
        IF NEW.expires_at IS NULL OR NEW.expires_at <= NEW.occurred_at THEN
            RAISE EXCEPTION 'courier offer expiry must be after creation';
        END IF;
        IF NEW.actor_type <> 'OPERATIONS' THEN
            RAISE EXCEPTION 'only operations may create a courier offer';
        END IF;
        SELECT status, customer_id INTO mission_status, offer_customer_id FROM missions
        WHERE mission_id = offer_mission_id FOR UPDATE;
        IF mission_status <> 'SEARCHING_COURIER' OR offer_customer_id IS DISTINCT FROM NEW.customer_id THEN
            RAISE EXCEPTION 'mission is not searching for a courier';
        END IF;
    ELSE
        IF current_offer_status IS DISTINCT FROM 'OFFERED' AND
            NOT (NEW.action IN ('CANCELLED', 'COMPLETED', 'RELEASED') AND current_offer_status = 'ACCEPTED') THEN
            RAISE EXCEPTION 'mission courier offer transition is invalid';
        END IF;
        IF NEW.action IN ('ACCEPTED', 'DECLINED') AND NEW.actor_type <> 'COURIER' THEN
            RAISE EXCEPTION 'only the offered courier may respond';
        END IF;
        IF NEW.action IN ('CANCELLED', 'COMPLETED') AND NEW.actor_type <> 'SYSTEM' THEN
            RAISE EXCEPTION 'system transition actor is required';
        END IF;
        IF NEW.action = 'RELEASED' AND current_offer_status <> 'ACCEPTED' THEN
            RAISE EXCEPTION 'only an accepted courier offer can be released';
        END IF;
        IF NEW.action = 'RELEASED' AND NOT (NEW.actor_type = 'OPERATIONS' AND NEW.reason = 'OPERATIONS_REASSIGNED_COURIER') THEN
            RAISE EXCEPTION 'authorized assignment release actor is required';
        END IF;
        IF NEW.action = 'EXPIRED' AND NOT (
            NEW.actor_type = 'OPERATIONS' AND NEW.reason = 'OPERATIONS_EXPIRED_OFFER') THEN
            RAISE EXCEPTION 'authorized offer expiry actor is required';
        END IF;
        IF NEW.action = 'WITHDRAWN' AND NOT (
            (NEW.actor_type = 'OPERATIONS' AND NEW.reason = 'OPERATIONS_WITHDREW_OFFER') OR
            (NEW.actor_type = 'SYSTEM' AND NEW.reason = 'COURIER_SUSPENDED')) THEN
            RAISE EXCEPTION 'authorized withdrawal actor is required';
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
        IF NEW.action IN ('ACCEPTED', 'WITHDRAWN', 'EXPIRED') AND mission_status <> 'SEARCHING_COURIER' THEN
            RAISE EXCEPTION 'mission is not searching for a courier';
        END IF;
        IF NEW.action = 'EXPIRED' AND (offer_expires_at IS NULL OR NEW.occurred_at < offer_expires_at) THEN
            RAISE EXCEPTION 'courier offer has not expired';
        END IF;
        IF NEW.action = 'RELEASED' AND mission_status NOT IN ('COURIER_ASSIGNED', 'ARRIVED_PICKUP') THEN
            RAISE EXCEPTION 'assignment can only be released before courier handoff';
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
            (offer_id, mission_id, customer_id, courier_id, zone_id, status, created_at, updated_at, last_event_id, expires_at)
        VALUES (NEW.offer_id, NEW.mission_id, NEW.customer_id, NEW.courier_id,
            NEW.zone_id, 'OFFERED', NEW.occurred_at, NEW.occurred_at, NEW.event_id, NEW.expires_at);
    ELSE
        UPDATE mission_courier_offers
        SET status = NEW.action, updated_at = NEW.occurred_at, last_event_id = NEW.event_id
        WHERE offer_id = NEW.offer_id;
    END IF;
    RETURN NULL;
END;
$apply_offer_event$;

