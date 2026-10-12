# Sprint 093 — Persistent Zone Geometry Snapshots

## Goal

Persist the validated and versioned zone boundaries from Sprint 092 in PostgreSQL with integrity checks and append-only behavior.

## Delivered

- Add migration `029_zone_geometry_versions.sql` for globally scoped immutable zone snapshots.
- Add `PostgresZoneGeometryRepository` for idempotent insert and integrity-verified read.
- Persist source/revision/attribution, geometry type/SRID/WKB, geometry digest, complete snapshot digest, and record time.
- Reject conflicting writes to an existing zone/version key; permit additional opaque versions.
- Register the repository and add PostgreSQL-backed integration tests for concurrent replay, conflict, retrieval, and mutation rejection.
- Update the zone backlog and storage contract documentation.

## Boundaries

No geometry is automatically activated. No OSM import, public API, tenant-specific zone model, location tracking, coverage decision, deployment, or production service change is included. Geometry versions remain unusable for delivery eligibility until lifecycle and authority rules are specified.

## Validation

- PostgreSQL integration tests verify persistence, concurrent exact replay, same-version conflict, read integrity, version append, and database-enforced update/delete rejection.
- CI is pending on this Draft PR.
