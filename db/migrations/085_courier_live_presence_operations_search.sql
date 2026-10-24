-- Extend privileged courier read auditing for the current-presence operations roster.
ALTER TABLE courier_operations_read_audit
    DROP CONSTRAINT IF EXISTS courier_operations_read_audit_action_check;
ALTER TABLE courier_operations_read_audit
    ADD CONSTRAINT courier_operations_read_audit_action_check
    CHECK (action IN (
        'COURIER_SEARCH', 'COURIER_DETAIL', 'COURIER_AUDIT_INSPECTION',
        'COURIER_AVAILABILITY_SEARCH', 'COURIER_CAPACITY_PROJECTION',
        'COURIER_PLANNING_ELIGIBILITY_SEARCH', 'COURIER_PRESENCE_SEARCH'));

ALTER TABLE courier_operations_read_audit
    DROP CONSTRAINT IF EXISTS courier_operations_read_audit_scope_check;
ALTER TABLE courier_operations_read_audit
    ADD CONSTRAINT courier_operations_read_audit_scope_check
    CHECK (
        (action IN (
            'COURIER_SEARCH', 'COURIER_AUDIT_INSPECTION',
            'COURIER_AVAILABILITY_SEARCH', 'COURIER_CAPACITY_PROJECTION',
            'COURIER_PLANNING_ELIGIBILITY_SEARCH', 'COURIER_PRESENCE_SEARCH')
            AND courier_id IS NULL) OR
        (action = 'COURIER_DETAIL' AND courier_id IS NOT NULL));

CREATE INDEX IF NOT EXISTS courier_live_presence_operations_search_idx
    ON courier_live_presence (is_online, zone_id, changed_at, courier_id);
