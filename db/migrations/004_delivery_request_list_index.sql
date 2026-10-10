-- Stable keyset pagination for customer-scoped delivery request history.
CREATE INDEX IF NOT EXISTS delivery_requests_customer_page_idx
    ON delivery_requests (customer_id, created_at DESC, delivery_request_id DESC);
