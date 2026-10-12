-- Permit the complete operational Mission lifecycle while retaining terminal status checks.
ALTER TABLE missions
    DROP CONSTRAINT IF EXISTS missions_status_check;
ALTER TABLE missions
    ADD CONSTRAINT missions_status_check
    CHECK (status IN (
        'PROMISED', 'SEARCHING_COURIER', 'COURIER_ASSIGNED', 'ARRIVED_PICKUP',
        'HANDED_TO_COURIER', 'PICKED_UP', 'IN_TRANSIT', 'ARRIVED_DROPOFF',
        'DELIVERED', 'FAILED', 'CANCELLED'));

ALTER TABLE outbox_events
    DROP CONSTRAINT IF EXISTS outbox_events_event_type_check;
ALTER TABLE outbox_events
    ADD CONSTRAINT outbox_events_event_type_check
    CHECK (event_type IN (
        'quote.issued', 'promise.committed', 'promise.revised', 'mission.created',
        'mission.searching_courier', 'mission.courier_assigned', 'mission.arrived_pickup',
        'mission.handed_to_courier', 'mission.picked_up', 'mission.in_transit',
        'mission.arrived_dropoff', 'mission.delivered', 'mission.failed', 'mission.cancelled'));

CREATE UNIQUE INDEX IF NOT EXISTS outbox_events_mission_sequence_uidx
    ON outbox_events (customer_id, aggregate_id, sequence)
    WHERE aggregate_type = 'mission';

CREATE TABLE IF NOT EXISTS mission_lifecycle_idempotency (
    customer_id text NOT NULL REFERENCES customers(customer_id),
    mission_id text NOT NULL REFERENCES missions(mission_id),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
    event_id text NOT NULL,
    event_type text NOT NULL CHECK (event_type LIKE 'mission.%'),
    sequence bigint NOT NULL CHECK (sequence > 1),
    result_status text NOT NULL CHECK (result_status IN (
        'SEARCHING_COURIER', 'COURIER_ASSIGNED', 'ARRIVED_PICKUP',
        'HANDED_TO_COURIER', 'PICKED_UP', 'IN_TRANSIT', 'ARRIVED_DROPOFF',
        'DELIVERED', 'FAILED', 'CANCELLED')),
    occurred_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (customer_id, mission_id, idempotency_key),
    UNIQUE (customer_id, mission_id, sequence)
);

CREATE OR REPLACE FUNCTION reject_mission_lifecycle_idempotency_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Mission lifecycle idempotency records are append-only';
END;
$$;

DROP TRIGGER IF EXISTS mission_lifecycle_idempotency_no_update_delete
    ON mission_lifecycle_idempotency;
CREATE TRIGGER mission_lifecycle_idempotency_no_update_delete
    BEFORE UPDATE OR DELETE ON mission_lifecycle_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_mission_lifecycle_idempotency_mutation();

DROP TRIGGER IF EXISTS mission_lifecycle_idempotency_no_truncate
    ON mission_lifecycle_idempotency;
CREATE TRIGGER mission_lifecycle_idempotency_no_truncate
    BEFORE TRUNCATE ON mission_lifecycle_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_mission_lifecycle_idempotency_mutation();
