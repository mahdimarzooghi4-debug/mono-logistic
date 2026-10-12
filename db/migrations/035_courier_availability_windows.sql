ALTER TABLE courier_operations_read_audit
    DROP CONSTRAINT IF EXISTS courier_operations_read_audit_action_check;
ALTER TABLE courier_operations_read_audit
    ADD CONSTRAINT courier_operations_read_audit_action_check
    CHECK (action IN (
        'COURIER_SEARCH', 'COURIER_DETAIL', 'COURIER_AUDIT_INSPECTION',
        'COURIER_AVAILABILITY_SEARCH'));

-- Planned courier operating windows for future capacity planning. These are not live location or dispatch eligibility signals.
CREATE TABLE IF NOT EXISTS courier_availability_windows (
    window_id text PRIMARY KEY CHECK (
        length(window_id) = 36 AND left(window_id, 4) = 'caw_' AND
        substring(window_id from 5) !~ '[^0-9a-f]'),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    zone_id text NOT NULL CHECK (length(btrim(zone_id)) BETWEEN 1 AND 128),
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    status text NOT NULL CHECK (status IN ('SCHEDULED', 'CANCELLED')),
    created_by text NOT NULL CHECK (length(btrim(created_by)) BETWEEN 1 AND 128),
    created_at timestamp with time zone NOT NULL,
    cancelled_by text NULL,
    cancelled_at timestamp with time zone NULL,
    cancellation_reason text NULL,
    CHECK (ends_at > starts_at),
    CHECK (ends_at <= starts_at + interval '16 hours'),
    CHECK (
        (status = 'SCHEDULED' AND cancelled_by IS NULL AND cancelled_at IS NULL AND cancellation_reason IS NULL) OR
        (status = 'CANCELLED' AND cancelled_by IS NOT NULL AND cancelled_at IS NOT NULL AND
            cancellation_reason IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS courier_availability_window_schedule_idx
    ON courier_availability_windows (starts_at, window_id)
    WHERE status = 'SCHEDULED';
CREATE INDEX IF NOT EXISTS courier_availability_window_courier_idx
    ON courier_availability_windows (courier_id, starts_at, window_id);

CREATE TABLE IF NOT EXISTS courier_availability_idempotency (
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    operation text NOT NULL CHECK (operation IN ('CREATE_WINDOW', 'CANCEL_WINDOW')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (length(request_hash) = 64 AND request_hash !~ '[^0-9a-f]'),
    response jsonb NOT NULL CHECK (jsonb_typeof(response) = 'object'),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (actor_id, operation, idempotency_key)
);

CREATE OR REPLACE FUNCTION enforce_courier_availability_window()
RETURNS trigger
LANGUAGE plpgsql
AS $courier_availability_window$
DECLARE
    current_status text;
    approved_zones jsonb;
BEGIN
    PERFORM 1 FROM couriers WHERE courier_id = NEW.courier_id FOR UPDATE;
    SELECT status, approved_zone_ids INTO current_status, approved_zones
    FROM couriers WHERE courier_id = NEW.courier_id;
    IF current_status IS DISTINCT FROM 'ACTIVE' OR
       NOT (approved_zones ? NEW.zone_id) OR
       NOT EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = NEW.zone_id) THEN
        RAISE EXCEPTION 'courier availability requires an active courier and approved active zone';
    END IF;

    IF EXISTS (
        SELECT 1 FROM courier_availability_windows existing
        WHERE existing.courier_id = NEW.courier_id
          AND existing.status = 'SCHEDULED'
          AND existing.starts_at < NEW.ends_at
          AND existing.ends_at > NEW.starts_at
          AND existing.window_id <> NEW.window_id) THEN
        RAISE EXCEPTION 'courier availability windows cannot overlap';
    END IF;
    RETURN NEW;
END;
$courier_availability_window$;

DROP TRIGGER IF EXISTS courier_availability_window_insert_guard ON courier_availability_windows;
CREATE TRIGGER courier_availability_window_insert_guard
    BEFORE INSERT ON courier_availability_windows
    FOR EACH ROW EXECUTE FUNCTION enforce_courier_availability_window();

CREATE OR REPLACE FUNCTION protect_courier_availability_window()
RETURNS trigger
LANGUAGE plpgsql
AS $courier_availability_immutable$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'courier availability windows cannot be deleted';
    END IF;
    IF OLD.status <> 'SCHEDULED' OR NEW.status <> 'CANCELLED' OR
       NEW.window_id IS DISTINCT FROM OLD.window_id OR
       NEW.courier_id IS DISTINCT FROM OLD.courier_id OR
       NEW.zone_id IS DISTINCT FROM OLD.zone_id OR
       NEW.starts_at IS DISTINCT FROM OLD.starts_at OR
       NEW.ends_at IS DISTINCT FROM OLD.ends_at OR
       NEW.created_by IS DISTINCT FROM OLD.created_by OR
       NEW.created_at IS DISTINCT FROM OLD.created_at OR
       NEW.cancelled_by IS NULL OR NEW.cancelled_at IS NULL OR
       NEW.cancellation_reason IS NULL THEN
        RAISE EXCEPTION 'courier availability windows may only be cancelled once';
    END IF;
    RETURN NEW;
END;
$courier_availability_immutable$;

DROP TRIGGER IF EXISTS courier_availability_window_immutable ON courier_availability_windows;
CREATE TRIGGER courier_availability_window_immutable
    BEFORE UPDATE OR DELETE ON courier_availability_windows
    FOR EACH ROW EXECUTE FUNCTION protect_courier_availability_window();

CREATE OR REPLACE FUNCTION reject_courier_availability_idempotency_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $courier_availability_idempotency$
BEGIN
    RAISE EXCEPTION 'courier availability idempotency is append-only';
END;
$courier_availability_idempotency$;

DROP TRIGGER IF EXISTS courier_availability_idempotency_immutable ON courier_availability_idempotency;
CREATE TRIGGER courier_availability_idempotency_immutable
    BEFORE UPDATE OR DELETE ON courier_availability_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_availability_idempotency_mutation();

DROP TRIGGER IF EXISTS courier_availability_idempotency_no_truncate ON courier_availability_idempotency;
CREATE TRIGGER courier_availability_idempotency_no_truncate
    BEFORE TRUNCATE ON courier_availability_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_availability_idempotency_mutation();
