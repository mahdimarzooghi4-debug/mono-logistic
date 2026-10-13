-- Structured courier issue reports with immutable events and an operations resolution path.
CREATE TABLE IF NOT EXISTS mission_courier_issue_reports (
    report_id text PRIMARY KEY CHECK (report_id ~ '^mci_[0-9a-f]{32}$'),
    mission_id text NOT NULL REFERENCES missions(mission_id),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    issue_code text NOT NULL CHECK (issue_code IN (
        'ACCESS_BLOCKED', 'RECIPIENT_UNAVAILABLE', 'PACKAGE_MISMATCH', 'SAFETY_CONCERN', 'OTHER')),
    status text NOT NULL CHECK (status IN ('OPEN', 'RESOLVED')),
    reported_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    last_event_id text NOT NULL,
    CHECK (updated_at >= reported_at)
);
CREATE INDEX IF NOT EXISTS mission_courier_issue_reports_mission_idx
    ON mission_courier_issue_reports (mission_id, reported_at DESC, report_id DESC);
CREATE INDEX IF NOT EXISTS mission_courier_issue_reports_open_mission_idx
    ON mission_courier_issue_reports (mission_id, reported_at, report_id)
    WHERE status = 'OPEN';

CREATE TABLE IF NOT EXISTS mission_courier_issue_events (
    event_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    event_id text PRIMARY KEY CHECK (event_id ~ '^mcie_[0-9a-f]{32}$'),
    report_id text NOT NULL CHECK (report_id ~ '^mci_[0-9a-f]{32}$'),
    mission_id text NOT NULL REFERENCES missions(mission_id),
    courier_id text NOT NULL REFERENCES couriers(courier_id),
    issue_code text NOT NULL CHECK (issue_code IN (
        'ACCESS_BLOCKED', 'RECIPIENT_UNAVAILABLE', 'PACKAGE_MISMATCH', 'SAFETY_CONCERN', 'OTHER')),
    action text NOT NULL CHECK (action IN ('REPORTED', 'RESOLVED')),
    actor_type text NOT NULL CHECK (actor_type IN ('COURIER', 'OPERATIONS')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 128),
    occurred_at timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS mission_courier_issue_events_history_idx
    ON mission_courier_issue_events (report_id, event_sequence);

CREATE TABLE IF NOT EXISTS mission_courier_issue_idempotency (
    scope_id text NOT NULL CHECK (length(btrim(scope_id)) BETWEEN 1 AND 128),
    operation text NOT NULL CHECK (operation IN ('REPORT_ISSUE', 'RESOLVE_ISSUE')),
    idempotency_key text NOT NULL CHECK (length(btrim(idempotency_key)) BETWEEN 1 AND 128),
    request_hash text NOT NULL CHECK (length(request_hash) = 64 AND request_hash !~ '[^0-9a-f]'),
    response jsonb NOT NULL CHECK (jsonb_typeof(response) = 'object'),
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (scope_id, operation, idempotency_key)
);

CREATE OR REPLACE FUNCTION protect_mission_courier_issue_projection()
RETURNS trigger LANGUAGE plpgsql AS $issue_projection$
BEGIN
    IF pg_trigger_depth() < 2 THEN
        RAISE EXCEPTION 'mission courier issue projection is maintained by events';
    END IF;
    RETURN NEW;
END;
$issue_projection$;
DROP TRIGGER IF EXISTS mission_courier_issue_projection_event_only ON mission_courier_issue_reports;
CREATE TRIGGER mission_courier_issue_projection_event_only
    BEFORE INSERT OR UPDATE OR DELETE ON mission_courier_issue_reports
    FOR EACH ROW EXECUTE FUNCTION protect_mission_courier_issue_projection();
DROP TRIGGER IF EXISTS mission_courier_issue_projection_no_truncate ON mission_courier_issue_reports;
CREATE TRIGGER mission_courier_issue_projection_no_truncate
    BEFORE TRUNCATE ON mission_courier_issue_reports
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();

CREATE OR REPLACE FUNCTION validate_mission_courier_issue_event()
RETURNS trigger LANGUAGE plpgsql AS $issue_event_guard$
DECLARE
    current_report mission_courier_issue_reports%ROWTYPE;
    mission_status text;
BEGIN
    IF NEW.action = 'REPORTED' THEN
        IF NEW.actor_type <> 'COURIER' OR NEW.actor_id <> NEW.courier_id THEN
            RAISE EXCEPTION 'only the assigned courier may report a mission issue';
        END IF;
        SELECT status INTO mission_status FROM missions
        WHERE mission_id = NEW.mission_id FOR UPDATE;
        IF mission_status NOT IN ('COURIER_ASSIGNED', 'ARRIVED_PICKUP', 'HANDED_TO_COURIER',
            'PICKED_UP', 'IN_TRANSIT', 'ARRIVED_DROPOFF') OR
           NOT EXISTS (SELECT 1 FROM mission_courier_offers offer
               WHERE offer.mission_id = NEW.mission_id AND offer.courier_id = NEW.courier_id
                 AND offer.status = 'ACCEPTED') THEN
            RAISE EXCEPTION 'courier has no active assigned Mission';
        END IF;
        IF EXISTS (SELECT 1 FROM mission_courier_issue_reports WHERE report_id = NEW.report_id) THEN
            RAISE EXCEPTION 'mission issue report already exists';
        END IF;
    ELSE
        SELECT * INTO current_report FROM mission_courier_issue_reports
        WHERE report_id = NEW.report_id FOR UPDATE;
        IF NOT FOUND OR current_report.status <> 'OPEN' OR NEW.actor_type <> 'OPERATIONS' OR
           NEW.mission_id IS DISTINCT FROM current_report.mission_id OR
           NEW.courier_id IS DISTINCT FROM current_report.courier_id OR
           NEW.issue_code IS DISTINCT FROM current_report.issue_code THEN
            RAISE EXCEPTION 'mission issue resolution is invalid';
        END IF;
    END IF;
    RETURN NEW;
END;
$issue_event_guard$;

CREATE OR REPLACE FUNCTION apply_mission_courier_issue_event()
RETURNS trigger LANGUAGE plpgsql AS $apply_issue_event$
BEGIN
    IF NEW.action = 'REPORTED' THEN
        INSERT INTO mission_courier_issue_reports
            (report_id, mission_id, courier_id, issue_code, status, reported_at, updated_at, last_event_id)
        VALUES (NEW.report_id, NEW.mission_id, NEW.courier_id, NEW.issue_code,
            'OPEN', NEW.occurred_at, NEW.occurred_at, NEW.event_id);
    ELSE
        UPDATE mission_courier_issue_reports
        SET status = 'RESOLVED', updated_at = NEW.occurred_at, last_event_id = NEW.event_id
        WHERE report_id = NEW.report_id;
    END IF;
    RETURN NULL;
END;
$apply_issue_event$;
DROP TRIGGER IF EXISTS mission_courier_issue_event_guard ON mission_courier_issue_events;
CREATE TRIGGER mission_courier_issue_event_guard BEFORE INSERT ON mission_courier_issue_events
    FOR EACH ROW EXECUTE FUNCTION validate_mission_courier_issue_event();
DROP TRIGGER IF EXISTS mission_courier_issue_event_apply ON mission_courier_issue_events;
CREATE TRIGGER mission_courier_issue_event_apply AFTER INSERT ON mission_courier_issue_events
    FOR EACH ROW EXECUTE FUNCTION apply_mission_courier_issue_event();
DROP TRIGGER IF EXISTS mission_courier_issue_events_immutable ON mission_courier_issue_events;
CREATE TRIGGER mission_courier_issue_events_immutable BEFORE UPDATE OR DELETE ON mission_courier_issue_events
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS mission_courier_issue_events_no_truncate ON mission_courier_issue_events;
CREATE TRIGGER mission_courier_issue_events_no_truncate BEFORE TRUNCATE ON mission_courier_issue_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS mission_courier_issue_idempotency_immutable ON mission_courier_issue_idempotency;
CREATE TRIGGER mission_courier_issue_idempotency_immutable BEFORE UPDATE OR DELETE ON mission_courier_issue_idempotency
    FOR EACH ROW EXECUTE FUNCTION reject_courier_append_only_mutation();
DROP TRIGGER IF EXISTS mission_courier_issue_idempotency_no_truncate ON mission_courier_issue_idempotency;
CREATE TRIGGER mission_courier_issue_idempotency_no_truncate BEFORE TRUNCATE ON mission_courier_issue_idempotency
    FOR EACH STATEMENT EXECUTE FUNCTION reject_courier_append_only_mutation();
