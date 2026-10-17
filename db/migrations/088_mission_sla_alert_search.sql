-- Extend the immutable Mission read ledger for delivery SLA alert page reads.
ALTER TABLE mission_operations_read_audit
    DROP CONSTRAINT IF EXISTS mission_operations_read_audit_action_check;
ALTER TABLE mission_operations_read_audit
    ADD CONSTRAINT mission_operations_read_audit_action_check
    CHECK (action IN (
        'MISSION_SEARCH', 'MISSION_DETAIL', 'MISSION_TIMELINE',
        'MISSION_AUDIT_INSPECTION', 'MISSION_COURIER_OFFER_HISTORY',
        'MISSION_COURIER_ISSUE_SEARCH', 'MISSION_COURIER_ISSUE_INBOX_SEARCH',
        'MISSION_SLA_ALERT_SEARCH', 'MISSION_DISPATCH_QUEUE',
        'MISSION_DISPATCH_ATTEMPT_HISTORY', 'MISSION_FARE_REVIEW'));
