-- Support global Mono-operations Mission search and filtered keyset pagination.
CREATE INDEX IF NOT EXISTS missions_operations_created_idx
    ON missions (created_at DESC, mission_id DESC);

CREATE INDEX IF NOT EXISTS missions_operations_status_created_idx
    ON missions (status, created_at DESC, mission_id DESC);
