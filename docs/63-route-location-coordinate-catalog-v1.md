# Route Location Coordinate Catalog v1

## Purpose

Persist the WGS84 coordinates referenced by stable `RouteLocationId` values so route matrix acquisition can resolve every route and candidate location. The catalog is provider-neutral and stores only location ID, coordinate, version, command ID, and record time.

## Versioning

- `RouteLocationCoordinateWriteCommand` requires a command ID and expected current version.
- A first write uses expected version zero. Each successful update appends one immutable coordinate version and advances the current pointer.
- Repeating the same command and payload returns `Replayed`; reusing the command ID with another payload or writing against a stale version returns `Conflict`.
- Reads by ID list return only current coordinates. Version reads preserve previous coordinates for audit and reproduce earlier evidence inputs.
- Missing IDs are omitted from resolver results; route matrix acquisition fails closed if any required ID is absent.

The schema is in migration `053_route_location_coordinate_catalog.sql`. Apply the migration before using the Postgres catalog. This module defines no external write endpoint, actor authorization, geocoding process, location ownership rule, or coordinate approval workflow. Those require separate product decisions and integration.

## Data constraints

IDs and command IDs are trimmed, non-empty values of at most 128 characters without control characters. Coordinates must be finite WGS84 latitude/longitude pairs. Version rows are append-only; current pointers can advance by exactly one version only after the corresponding immutable version row exists.
