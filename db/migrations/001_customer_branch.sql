-- Mono customer and branch foundation. Apply once to a new Mono PostgreSQL database.
CREATE TABLE customers (
    customer_id text PRIMARY KEY CHECK (customer_id ~ '^cus_[0-9a-f]{32}$'),
    legal_name text NOT NULL CHECK (length(btrim(legal_name)) > 0),
    display_name text NULL,
    status text NOT NULL CHECK (length(btrim(status)) > 0),
    customer_type text NULL,
    default_channel text NULL,
    default_currency text NULL,
    created_at timestamp with time zone NOT NULL
);

CREATE TABLE branches (
    branch_id text PRIMARY KEY CHECK (branch_id ~ '^br_[0-9a-f]{32}$'),
    customer_id text NOT NULL REFERENCES customers(customer_id),
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    address text NULL,
    latitude double precision NULL CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
    longitude double precision NULL CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
    zone_id text NULL,
    operating_hours jsonb NULL,
    status text NOT NULL CHECK (length(btrim(status)) > 0),
    CONSTRAINT branches_coordinates_paired
        CHECK ((latitude IS NULL) = (longitude IS NULL))
);

CREATE INDEX branches_customer_id_idx ON branches(customer_id);
CREATE INDEX branches_customer_name_idx ON branches(customer_id, name);
