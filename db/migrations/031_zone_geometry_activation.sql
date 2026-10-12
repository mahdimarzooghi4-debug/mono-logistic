-- Administrative activation pointer and append-only lifecycle history.
CREATE TABLE IF NOT EXISTS zone_geometry_activation_events (
    activation_event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    zone_id text NOT NULL,
    geometry_version text NOT NULL,
    snapshot_sha256 text NOT NULL CHECK (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    action text NOT NULL CHECK (action IN ('ACTIVATE', 'DEACTIVATE')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 256),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 1000),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT zone_geometry_activation_snapshot_fk
        FOREIGN KEY (zone_id, geometry_version)
        REFERENCES zone_geometry_versions (zone_id, geometry_version)
);

CREATE INDEX IF NOT EXISTS zone_geometry_activation_history_idx
    ON zone_geometry_activation_events (zone_id, activation_event_id);

CREATE TABLE IF NOT EXISTS zone_geometry_active (
    zone_id text PRIMARY KEY,
    geometry_version text NOT NULL,
    snapshot_sha256 text NOT NULL CHECK (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    activated_at timestamp with time zone NOT NULL,
    activated_by text NOT NULL CHECK (length(btrim(activated_by)) BETWEEN 1 AND 256),
    CONSTRAINT zone_geometry_active_snapshot_fk
        FOREIGN KEY (zone_id, geometry_version)
        REFERENCES zone_geometry_versions (zone_id, geometry_version)
);

CREATE OR REPLACE FUNCTION validate_zone_geometry_activation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    expected_hash text;
    latest_decision text;
BEGIN
    SELECT snapshot_sha256 INTO expected_hash
    FROM zone_geometry_versions
    WHERE zone_id = NEW.zone_id AND geometry_version = NEW.geometry_version;
    IF expected_hash IS NULL OR expected_hash <> NEW.snapshot_sha256 THEN
        RAISE EXCEPTION 'zone geometry activation must reference the exact snapshot hash';
    END IF;

    IF TG_TABLE_NAME = 'zone_geometry_activation_events' AND NEW.action = 'ACTIVATE'
       OR TG_TABLE_NAME = 'zone_geometry_active' THEN
        SELECT decision INTO latest_decision
        FROM zone_geometry_reviews
        WHERE zone_id = NEW.zone_id AND geometry_version = NEW.geometry_version
        ORDER BY review_id DESC LIMIT 1;
        IF latest_decision IS DISTINCT FROM 'APPROVE' THEN
            RAISE EXCEPTION 'zone geometry activation requires a latest APPROVE review';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS zone_geometry_activation_event_valid ON zone_geometry_activation_events;
CREATE TRIGGER zone_geometry_activation_event_valid
    BEFORE INSERT ON zone_geometry_activation_events
    FOR EACH ROW EXECUTE FUNCTION validate_zone_geometry_activation();

DROP TRIGGER IF EXISTS zone_geometry_active_valid ON zone_geometry_active;
CREATE TRIGGER zone_geometry_active_valid
    BEFORE INSERT OR UPDATE ON zone_geometry_active
    FOR EACH ROW EXECUTE FUNCTION validate_zone_geometry_activation();

CREATE OR REPLACE FUNCTION reject_zone_geometry_activation_event_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'zone geometry activation events are append-only';
END;
$$;

DROP TRIGGER IF EXISTS zone_geometry_activation_events_immutable ON zone_geometry_activation_events;
CREATE TRIGGER zone_geometry_activation_events_immutable
    BEFORE UPDATE OR DELETE ON zone_geometry_activation_events
    FOR EACH ROW EXECUTE FUNCTION reject_zone_geometry_activation_event_mutation();

DROP TRIGGER IF EXISTS zone_geometry_activation_events_no_truncate ON zone_geometry_activation_events;
CREATE TRIGGER zone_geometry_activation_events_no_truncate
    BEFORE TRUNCATE ON zone_geometry_activation_events
    FOR EACH STATEMENT EXECUTE FUNCTION reject_zone_geometry_activation_event_mutation();
