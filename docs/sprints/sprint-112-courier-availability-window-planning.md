# Sprint 112 — Courier availability window planning

## Goal

Give Mono operations a durable, zone-aware way to plan courier capacity while keeping scheduled windows separate from real-time availability and dispatch eligibility.

## Scope

- Add staff-managed future availability windows for ACTIVE couriers in their approved, currently active zones.
- Bound each window to at most 16 hours and its start to the next 30 days.
- Prevent overlapping windows for the same courier, including concurrent writes.
- Add idempotent creation and one-way cancellation with immutable schedule fields.
- Add bounded, keyset-paginated time-range search and audit every search.
- Keep GPS, OSM, courier self-service, and dispatch assignment out of scope.

## Acceptance criteria

- Only an ACTIVE courier can receive a planned window.
- A zone must be approved for that courier and active in Mono's zone registry.
- Duplicate requests replay the original result; changed content on the same idempotency key conflicts.
- A courier cannot have overlapping scheduled windows.
- Cancellation retains the original window and records actor, time, and reason; schedule fields cannot be edited or deleted.
- Search is limited to 100 items and a maximum 31-day interval, and fails closed if read-audit persistence fails.
- PostgreSQL integration tests cover create/replay, overlap rejection, listing, cancellation/replay, and mutation protection.

## Data boundary

The schedule stores internal Courier ID, approved zone ID, UTC time window, status, staff attribution, and cancellation reason. It stores no location, route, customer, or personal information.

## Operational boundary

A scheduled window is a planning declaration only. It does not make a courier live, available, authenticated, or dispatch eligible. OSM/GPS, Stage, and deployment remain deferred.

## Validation

- PostgreSQL integration tests use the isolated courier schema with migrations 033, 034, and 035.
- Full CI runs .NET build/tests and the container image build.
