-- Versioned, effective-dated configuration records for a customer's enabled Mono service.
CREATE TABLE IF NOT EXISTS customer_service_configurations (
    service_configuration_id text PRIMARY KEY
        CHECK (service_configuration_id ~ '^scf_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    service_code text NOT NULL
        CHECK (service_code ~ '^[A-Z][A-Z0-9_]{0,63}$'),
    version integer NOT NULL CHECK (version > 0),
    status text NOT NULL CHECK (status IN ('DRAFT', 'ACTIVE', 'INACTIVE', 'RETIRED')),
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone NULL,
    configuration jsonb NOT NULL CHECK (jsonb_typeof(configuration) = 'object'),
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT customer_service_configurations_effective_range
        CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT customer_service_configurations_version_unique
        UNIQUE (customer_id, service_code, version)
);

CREATE INDEX IF NOT EXISTS customer_service_configurations_effective_idx
    ON customer_service_configurations
        (customer_id, service_code, status, effective_from DESC, version DESC);
