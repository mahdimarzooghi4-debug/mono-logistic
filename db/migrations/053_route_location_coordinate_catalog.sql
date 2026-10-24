-- Versioned coordinate catalog for stable route location IDs.
CREATE TABLE IF NOT EXISTS route_location_coordinates (
    location_id text PRIMARY KEY CHECK (length(btrim(location_id)) BETWEEN 1 AND 128),
    current_version integer NOT NULL DEFAULT 0 CHECK (current_version >= 0),
    created_at timestamp with time zone NOT NULL
);

CREATE TABLE IF NOT EXISTS route_location_coordinate_versions (
    location_id text NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    command_id text NOT NULL UNIQUE CHECK (length(btrim(command_id)) BETWEEN 1 AND 128),
    expected_version integer NOT NULL CHECK (expected_version >= 0),
    latitude double precision NOT NULL CHECK (latitude BETWEEN -90 AND 90),
    longitude double precision NOT NULL CHECK (longitude BETWEEN -180 AND 180),
    recorded_at timestamp with time zone NOT NULL,
    PRIMARY KEY (location_id, version),
    FOREIGN KEY (location_id) REFERENCES route_location_coordinates(location_id),
    CHECK (version = expected_version + 1)
);

CREATE OR REPLACE FUNCTION validate_route_location_coordinate_version_insert()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    current_head integer;
BEGIN
    SELECT current_version INTO current_head
    FROM route_location_coordinates WHERE location_id = NEW.location_id FOR UPDATE;
    IF current_head IS NULL OR current_head <> NEW.expected_version THEN
        RAISE EXCEPTION 'route location coordinate version must extend the current head';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS route_location_coordinate_version_extends_head ON route_location_coordinate_versions;
CREATE TRIGGER route_location_coordinate_version_extends_head
    BEFORE INSERT ON route_location_coordinate_versions
    FOR EACH ROW EXECUTE FUNCTION validate_route_location_coordinate_version_insert();

CREATE OR REPLACE FUNCTION validate_route_location_coordinate_head_advance()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.current_version <> 0 THEN
            RAISE EXCEPTION 'new route locations must begin at version zero';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.location_id <> OLD.location_id OR NEW.created_at <> OLD.created_at OR
       NEW.current_version <> OLD.current_version + 1 OR
       NOT EXISTS (
           SELECT 1 FROM route_location_coordinate_versions
           WHERE location_id = NEW.location_id AND version = NEW.current_version
       ) THEN
        RAISE EXCEPTION 'route location coordinate head must advance by one recorded version';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS route_location_coordinate_head_advance ON route_location_coordinates;
CREATE TRIGGER route_location_coordinate_head_advance
    BEFORE INSERT OR UPDATE ON route_location_coordinates
    FOR EACH ROW EXECUTE FUNCTION validate_route_location_coordinate_head_advance();

CREATE OR REPLACE FUNCTION reject_route_location_coordinate_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'route location coordinate versions are append-only';
END;
$$;

DROP TRIGGER IF EXISTS route_location_coordinate_versions_immutable ON route_location_coordinate_versions;
CREATE TRIGGER route_location_coordinate_versions_immutable
    BEFORE UPDATE OR DELETE ON route_location_coordinate_versions
    FOR EACH ROW EXECUTE FUNCTION reject_route_location_coordinate_mutation();
DROP TRIGGER IF EXISTS route_location_coordinate_versions_no_truncate ON route_location_coordinate_versions;
CREATE TRIGGER route_location_coordinate_versions_no_truncate
    BEFORE TRUNCATE ON route_location_coordinate_versions
    FOR EACH STATEMENT EXECUTE FUNCTION reject_route_location_coordinate_mutation();

DROP TRIGGER IF EXISTS route_location_coordinates_no_delete ON route_location_coordinates;
CREATE TRIGGER route_location_coordinates_no_delete
    BEFORE DELETE OR TRUNCATE ON route_location_coordinates
    FOR EACH STATEMENT EXECUTE FUNCTION reject_route_location_coordinate_mutation();
