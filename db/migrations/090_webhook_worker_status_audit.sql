-- Record administrator reads of the in-process webhook delivery worker status.
ALTER TABLE webhook_operations_read_audit
    DROP CONSTRAINT IF EXISTS webhook_operations_read_audit_action_check;
ALTER TABLE webhook_operations_read_audit
    ADD CONSTRAINT webhook_operations_read_audit_action_check
        CHECK (action IN (
            'QUEUE_SUMMARY', 'QUEUE_SEARCH', 'DELIVERY_DETAIL', 'DELIVERY_REPLAY',
            'DELIVERY_CANCEL', 'AUDIT_HISTORY_READ', 'WORKER_STATUS_READ'));
