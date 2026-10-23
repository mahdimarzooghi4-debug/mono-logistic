CREATE TABLE courier_current_locations (
    courier_id text PRIMARY KEY REFERENCES couriers(courier_id) ON DELETE CASCADE,
    latitude double precision NOT NULL CHECK (latitude BETWEEN -90 AND 90),
    longitude double precision NOT NULL CHECK (longitude BETWEEN -180 AND 180),
    accuracy_meters double precision NULL CHECK (accuracy_meters IS NULL OR accuracy_meters >= 0),
    heading_degrees double precision NULL CHECK (heading_degrees IS NULL OR (heading_degrees >= 0 AND heading_degrees < 360)),
    speed_meters_per_second double precision NULL CHECK (speed_meters_per_second IS NULL OR speed_meters_per_second >= 0),
    observed_at timestamptz NOT NULL,
    active_route_id text NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX courier_current_locations_observed_at_idx
    ON courier_current_locations (observed_at);
