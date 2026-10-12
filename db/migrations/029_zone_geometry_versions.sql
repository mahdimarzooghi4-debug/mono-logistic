-- Globally scoped, immutable operational zone-geometry snapshots.
CREATE TABLE IF NOT EXISTS zone_geometry_versions (
    zone_id text NOT NULL CHECK (length(btrim(zone_id)) BETWEEN 1 AND 128),
    zone_kind text NOT NULL CHECK (zone_kind IN ('City', 'OperatingZone', 'MicroZone')),
    geometry_version text NOT NULL CHECK (length(btrim(geometry_version)) BETWEEN 1 AND 128),
    source_id text NOT NULL CHECK (length(btrim(source_id)) BETWEEN 1 AND 128),
    source_revision text NOT NULL CHECK (length(btrim(source_revision)) BETWEEN 1 AND 256),
    attribution text NOT NULL CHECK (length(btrim(attribution)) BETWEEN 1 AND 512),
    geometry_srid integer NOT NULL CHECK (geometry_srid = 4326),
    geometry_type text NOT NULL CHECK (geometry_type IN ('Polygon', 'MultiPolygon')),
    geometry_wkb bytea NOT NULL CHECK (octet_length(geometry_wkb) > 0),
    geometry_sha256 text NOT NULL CHECK (geometry_sha256 ~ '^[0-9a-f]{64}$'),
    snapshot_sha256 text NOT NULL CHECK (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    recorded_at timestamp with time zone NOT NULL,
    PRIMARY KEY (zone_id, geometry_version)
);

CREATE INDEX IF NOT EXISTS zone_geometry_versions_zone_recorded_idx
    ON zone_geometry_versions (zone_id, recorded_at DESC, geometry_version DESC);

CREATE OR REPLACE FUNCTION reject_zone_geometry_version_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'zone geometry versions are append-only';
END;
$$;

DROP TRIGGER IF EXISTS zone_geometry_versions_immutable
    ON zone_geometry_versions;

CREATE TRIGGER zone_geometry_versions_immutable
    BEFORE UPDATE OR DELETE ON zone_geometry_versions
    FOR EACH ROW
    EXECUTE FUNCTION reject_zone_geometry_version_mutation();
