-- Supports tenant-scoped keyset pagination for webhook delivery history.
CREATE INDEX IF NOT EXISTS webhook_deliveries_customer_history_idx
    ON webhook_deliveries (customer_id, created_at DESC, delivery_id DESC);
