-- Human decisions are append-only and bound to an exact immutable geometry snapshot.
CREATE TABLE IF NOT EXISTS zone_geometry_reviews (
    review_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    zone_id text NOT NULL,
    geometry_version text NOT NULL,
    snapshot_sha256 text NOT NULL CHECK (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    decision text NOT NULL CHECK (decision IN ('APPROVE', 'REJECT')),
    actor_id text NOT NULL CHECK (length(btrim(actor_id)) BETWEEN 1 AND 256),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 1000),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT zone_geometry_reviews_snapshot_fk
        FOREIGN KEY (zone_id, geometry_version)
        REFERENCES zone_geometry_versions (zone_id, geometry_version)
);

CREATE INDEX IF NOT EXISTS zone_geometry_reviews_history_idx
    ON zone_geometry_reviews (zone_id, geometry_version, review_id);

CREATE OR REPLACE FUNCTION validate_zone_geometry_review_snapshot_hash()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    expected_hash text;
BEGIN
    SELECT snapshot_sha256 INTO expected_hash
    FROM zone_geometry_versions
    WHERE zone_id = NEW.zone_id AND geometry_version = NEW.geometry_version;
    IF expected_hash IS NULL OR expected_hash <> NEW.snapshot_sha256 THEN
        RAISE EXCEPTION 'zone geometry review must reference the exact snapshot hash';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS zone_geometry_reviews_exact_snapshot ON zone_geometry_reviews;
CREATE TRIGGER zone_geometry_reviews_exact_snapshot
    BEFORE INSERT ON zone_geometry_reviews
    FOR EACH ROW EXECUTE FUNCTION validate_zone_geometry_review_snapshot_hash();

CREATE OR REPLACE FUNCTION reject_zone_geometry_review_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'zone geometry reviews are append-only';
END;
$$;

DROP TRIGGER IF EXISTS zone_geometry_reviews_immutable ON zone_geometry_reviews;
CREATE TRIGGER zone_geometry_reviews_immutable
    BEFORE UPDATE OR DELETE ON zone_geometry_reviews
    FOR EACH ROW EXECUTE FUNCTION reject_zone_geometry_review_mutation();

DROP TRIGGER IF EXISTS zone_geometry_reviews_no_truncate ON zone_geometry_reviews;
CREATE TRIGGER zone_geometry_reviews_no_truncate
    BEFORE TRUNCATE ON zone_geometry_reviews
    FOR EACH STATEMENT EXECUTE FUNCTION reject_zone_geometry_review_mutation();
