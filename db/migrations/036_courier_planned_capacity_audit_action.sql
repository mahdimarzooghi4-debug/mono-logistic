-- Record capacity projection reads in the existing immutable courier operations ledger.
ALTER TABLE courier_operations_read_audit
    DROP CONSTRAINT IF EXISTS courier_operations_read_audit_action_check;
ALTER TABLE courier_operations_read_audit
    ADD CONSTRAINT courier_operations_read_audit_action_check
    CHECK (action IN (
        'COURIER_SEARCH', 'COURIER_DETAIL', 'COURIER_AUDIT_INSPECTION',
        'COURIER_AVAILABILITY_SEARCH', 'COURIER_CAPACITY_PROJECTION'));

ALTER TABLE courier_operations_read_audit
    DROP CONSTRAINT IF EXISTS courier_operations_read_audit_scope_check;
ALTER TABLE courier_operations_read_audit
    ADD CONSTRAINT courier_operations_read_audit_scope_check
    CHECK (
        (action IN (
            'COURIER_SEARCH', 'COURIER_AUDIT_INSPECTION',
            'COURIER_AVAILABILITY_SEARCH', 'COURIER_CAPACITY_PROJECTION')
            AND courier_id IS NULL) OR
        (action = 'COURIER_DETAIL' AND courier_id IS NOT NULL));
