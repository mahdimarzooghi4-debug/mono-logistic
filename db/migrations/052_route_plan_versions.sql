-- Immutable, ordered route-plan snapshots with a guarded current-version pointer.
CREATE TABLE IF NOT EXISTS routes (
    route_id text PRIMARY KEY CHECK (length(btrim(route_id)) BETWEEN 1 AND 128),
    current_version integer NOT NULL DEFAULT 0 CHECK (current_version >= 0),
    created_at timestamp with time zone NOT NULL
);

CREATE TABLE IF NOT EXISTS route_plan_versions (
    route_id text NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    command_id text NOT NULL UNIQUE CHECK (length(btrim(command_id)) BETWEEN 1 AND 128),
    expected_version integer NOT NULL CHECK (expected_version >= 0),
    start_location_id text NOT NULL CHECK (length(btrim(start_location_id)) BETWEEN 1 AND 128),
    started_at timestamp with time zone NOT NULL,
    snapshot_sha256 text NOT NULL CHECK (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    recorded_at timestamp with time zone NOT NULL,
    PRIMARY KEY (route_id, version),
    FOREIGN KEY (route_id) REFERENCES routes(route_id),
    CHECK (version = expected_version + 1)
);

CREATE INDEX IF NOT EXISTS route_plan_versions_history_idx
    ON route_plan_versions (route_id, version DESC);

CREATE OR REPLACE FUNCTION validate_route_plan_version_insert()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    current_head integer;
BEGIN
    SELECT current_version INTO current_head
    FROM routes WHERE route_id = NEW.route_id FOR UPDATE;
    IF current_head IS NULL OR current_head <> NEW.expected_version THEN
        RAISE EXCEPTION 'route plan version must extend the current route head';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS route_plan_version_extends_head ON route_plan_versions;
CREATE TRIGGER route_plan_version_extends_head
    BEFORE INSERT ON route_plan_versions
    FOR EACH ROW EXECUTE FUNCTION validate_route_plan_version_insert();

CREATE TABLE IF NOT EXISTS route_plan_version_missions (
    route_id text NOT NULL,
    version integer NOT NULL,
    mission_id text NOT NULL CHECK (length(btrim(mission_id)) BETWEEN 1 AND 128),
    committed_delivery_at timestamp with time zone NOT NULL,
    PRIMARY KEY (route_id, version, mission_id),
    FOREIGN KEY (route_id, version)
        REFERENCES route_plan_versions(route_id, version)
);

CREATE TABLE IF NOT EXISTS route_plan_version_stops (
    route_id text NOT NULL,
    version integer NOT NULL,
    stop_order smallint NOT NULL CHECK (stop_order >= 0),
    mission_id text NOT NULL,
    stop_kind text NOT NULL CHECK (stop_kind IN ('Pickup', 'Dropoff')),
    location_id text NOT NULL CHECK (length(btrim(location_id)) BETWEEN 1 AND 128),
    service_seconds integer NOT NULL CHECK (service_seconds >= 0),
    pickup_ready_at timestamp with time zone NULL,
    PRIMARY KEY (route_id, version, stop_order),
    FOREIGN KEY (route_id, version, mission_id)
        REFERENCES route_plan_version_missions(route_id, version, mission_id),
    CHECK (
        (stop_kind = 'Pickup' AND pickup_ready_at IS NOT NULL AND service_seconds <= 180)
        OR (stop_kind = 'Dropoff' AND pickup_ready_at IS NULL)
    )
);

CREATE OR REPLACE FUNCTION reject_route_plan_version_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'route plan versions are append-only';
END;
$$;

DROP TRIGGER IF EXISTS route_plan_versions_immutable ON route_plan_versions;
CREATE TRIGGER route_plan_versions_immutable
    BEFORE UPDATE OR DELETE ON route_plan_versions
    FOR EACH ROW EXECUTE FUNCTION reject_route_plan_version_mutation();
DROP TRIGGER IF EXISTS route_plan_versions_no_truncate ON route_plan_versions;
CREATE TRIGGER route_plan_versions_no_truncate
    BEFORE TRUNCATE ON route_plan_versions
    FOR EACH STATEMENT EXECUTE FUNCTION reject_route_plan_version_mutation();

DROP TRIGGER IF EXISTS route_plan_version_missions_immutable ON route_plan_version_missions;
CREATE TRIGGER route_plan_version_missions_immutable
    BEFORE UPDATE OR DELETE ON route_plan_version_missions
    FOR EACH ROW EXECUTE FUNCTION reject_route_plan_version_mutation();
DROP TRIGGER IF EXISTS route_plan_version_missions_no_truncate ON route_plan_version_missions;
CREATE TRIGGER route_plan_version_missions_no_truncate
    BEFORE TRUNCATE ON route_plan_version_missions
    FOR EACH STATEMENT EXECUTE FUNCTION reject_route_plan_version_mutation();

DROP TRIGGER IF EXISTS route_plan_stops_immutable ON route_plan_version_stops;
CREATE TRIGGER route_plan_stops_immutable
    BEFORE UPDATE OR DELETE ON route_plan_version_stops
    FOR EACH ROW EXECUTE FUNCTION reject_route_plan_version_mutation();
DROP TRIGGER IF EXISTS route_plan_stops_no_truncate ON route_plan_version_stops;
CREATE TRIGGER route_plan_stops_no_truncate
    BEFORE TRUNCATE ON route_plan_version_stops
    FOR EACH STATEMENT EXECUTE FUNCTION reject_route_plan_version_mutation();

CREATE OR REPLACE FUNCTION validate_route_current_version_advance()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.current_version <> 0 THEN
            RAISE EXCEPTION 'new routes must begin at version zero';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.route_id <> OLD.route_id OR NEW.created_at <> OLD.created_at OR
       NEW.current_version <> OLD.current_version + 1 OR
       NOT EXISTS (
           SELECT 1 FROM route_plan_versions
           WHERE route_id = NEW.route_id AND version = NEW.current_version
             AND expected_version = OLD.current_version
       ) THEN
        RAISE EXCEPTION 'route current version must advance by one to an existing immutable version';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS routes_current_version_valid ON routes;
CREATE TRIGGER routes_current_version_valid
    BEFORE INSERT OR UPDATE ON routes
    FOR EACH ROW EXECUTE FUNCTION validate_route_current_version_advance();

CREATE OR REPLACE FUNCTION reject_route_row_deletion()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'route version heads cannot be deleted or truncated';
END;
$$;
DROP TRIGGER IF EXISTS routes_no_delete ON routes;
CREATE TRIGGER routes_no_delete
    BEFORE DELETE ON routes
    FOR EACH ROW EXECUTE FUNCTION reject_route_row_deletion();
DROP TRIGGER IF EXISTS routes_no_truncate ON routes;
CREATE TRIGGER routes_no_truncate
    BEFORE TRUNCATE ON routes
    FOR EACH STATEMENT EXECUTE FUNCTION reject_route_row_deletion();
