-- Supports prefix searches for customer IDs and case-folded customer names.
CREATE INDEX IF NOT EXISTS customers_customer_id_prefix_idx
    ON customers (customer_id text_pattern_ops);

CREATE INDEX IF NOT EXISTS customers_legal_name_prefix_idx
    ON customers (lower(legal_name) text_pattern_ops);

CREATE INDEX IF NOT EXISTS customers_display_name_prefix_idx
    ON customers (lower(display_name) text_pattern_ops);
