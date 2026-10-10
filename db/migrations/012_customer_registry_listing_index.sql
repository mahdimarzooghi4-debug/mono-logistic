-- Supports stable keyset pagination of the Mono customer registry.
CREATE INDEX IF NOT EXISTS customers_created_at_customer_id_idx
    ON customers (created_at DESC, customer_id DESC);
