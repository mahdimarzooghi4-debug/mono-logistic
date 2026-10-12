-- Allow immutable Quote issuance events and backfill existing Quote versions into the outbox.
ALTER TABLE outbox_events
    DROP CONSTRAINT IF EXISTS outbox_events_event_type_check;
ALTER TABLE outbox_events
    ADD CONSTRAINT outbox_events_event_type_check
    CHECK (event_type IN ('quote.issued', 'promise.committed', 'promise.revised', 'mission.created'));

INSERT INTO outbox_events (
    event_id, event_type, event_version, occurred_at, producer, correlation_id, causation_id,
    customer_id, aggregate_type, aggregate_id, sequence, data, created_at)
SELECT
    'evt_' || md5(q.customer_id || ':' || q.quote_id || ':' || q.version::text),
    'quote.issued',
    1,
    q.created_at,
    'mono-api',
    q.delivery_request_id,
    NULL,
    q.customer_id,
    'quote',
    q.quote_id,
    q.version,
    jsonb_build_object(
        'quote_id', q.quote_id,
        'delivery_request_id', q.delivery_request_id,
        'version', q.version),
    q.created_at
FROM delivery_quote_versions q
WHERE NOT EXISTS (
    SELECT 1 FROM outbox_events e
    WHERE e.aggregate_type = 'quote'
      AND e.aggregate_id = q.quote_id
      AND e.sequence = q.version)
ON CONFLICT DO NOTHING;
