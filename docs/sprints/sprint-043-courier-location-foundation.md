# Sprint 043 — Courier Location Foundation

- Status: In progress
- Phase: B — Logistics Core
- Epic: E5 — Maps, Geocoding & Location
- Capability: E5.3 — Courier Location Ingestion
- Branch: sprint-043-courier-location-foundation
- Base: sprint-042-location-resolution

## Sprint goal

Build a validated courier GPS update contract, deterministic current-position projection primitive, and freshness evaluation without enabling live collection before its security and domain prerequisites exist.

## Scope

- Define courier location update fields following the approved location architecture.
- Validate WGS84 coordinates and optional sensor values.
- Normalize courier and route identifiers and observed timestamps.
- Advance a per-courier projection only for strictly newer observations.
- Evaluate freshness against an explicit caller-supplied maximum age.
- Document privacy, identity, Mission/Route, and retention boundaries.

## Acceptance criteria

1. Missing courier ID or observed timestamp is rejected.
2. Invalid coordinates and optional accuracy, heading, or speed are rejected.
3. Out-of-order and equal-time observations do not replace the current position.
4. Freshness uses a supplied threshold; future-dated observations are not fresh.
5. The projection primitive is deterministic and does not persist or expose raw GPS.
6. No GPS endpoint is exposed until courier authentication, Mission/Route lifecycle, operational tracking authorization, and retention policy are available.
7. No default freshness threshold is introduced.

## Out of scope

Courier identity/authentication, persistence, GPS endpoint, location history, live tracking, assignment or coverage decisions, customer-facing location events, retention duration, Stage, and deployment.
