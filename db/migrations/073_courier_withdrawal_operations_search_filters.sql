-- Preserve the full filter context for privileged courier-withdrawal searches.
ALTER TABLE courier_withdrawal_operations_search_audit
    ADD COLUMN IF NOT EXISTS created_from_filter timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS created_to_filter timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS min_amount_toman_filter bigint NULL,
    ADD COLUMN IF NOT EXISTS max_amount_toman_filter bigint NULL;

ALTER TABLE courier_withdrawal_operations_search_audit
    DROP CONSTRAINT IF EXISTS courier_withdrawal_operations_search_audit_created_range_check;
ALTER TABLE courier_withdrawal_operations_search_audit
    ADD CONSTRAINT courier_withdrawal_operations_search_audit_created_range_check CHECK (
        (created_from_filter IS NULL) = (created_to_filter IS NULL) AND
        (created_from_filter IS NULL OR created_from_filter < created_to_filter));

ALTER TABLE courier_withdrawal_operations_search_audit
    DROP CONSTRAINT IF EXISTS courier_withdrawal_operations_search_audit_amount_range_check;
ALTER TABLE courier_withdrawal_operations_search_audit
    ADD CONSTRAINT courier_withdrawal_operations_search_audit_amount_range_check CHECK (
        (min_amount_toman_filter IS NULL OR min_amount_toman_filter >= 0) AND
        (max_amount_toman_filter IS NULL OR max_amount_toman_filter >= 0) AND
        (min_amount_toman_filter IS NULL OR max_amount_toman_filter IS NULL OR
         min_amount_toman_filter <= max_amount_toman_filter));
