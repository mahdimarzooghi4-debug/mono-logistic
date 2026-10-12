-- Permit branch maintenance to append an update event to the immutable audit history.
ALTER TABLE branch_operations_audit
    DROP CONSTRAINT IF EXISTS branch_operations_audit_action_check;

ALTER TABLE branch_operations_audit
    ADD CONSTRAINT branch_operations_audit_action_check
    CHECK (action IN ('CREATED', 'UPDATED'));
