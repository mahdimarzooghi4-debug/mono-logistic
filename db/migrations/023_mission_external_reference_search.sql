-- Prefix search for the operations Mission lookup; the external reference remains response-private.
CREATE INDEX IF NOT EXISTS delivery_requests_external_reference_prefix_idx
    ON delivery_requests (lower(external_reference) text_pattern_ops, delivery_request_id);
