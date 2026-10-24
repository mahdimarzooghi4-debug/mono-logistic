# Sprint 118 — Courier live presence

## Goal

Give an authenticated courier a minimal way to report whether they are currently online for operations, independently of staff-planned availability windows.

## Contract

- `GET /courier/v1/presence` returns the authenticated courier's current status.
- `POST /courier/v1/presence/online` accepts one approved `zone_id` and an `Idempotency-Key`.
- `POST /courier/v1/presence/offline` accepts an `Idempotency-Key`.
- Every courier endpoint requires Mono's validated courier authorization policy. The courier ID comes from the bound Mono identity, never from the request.
- `GET /operations/v1/couriers/{courierId}/presence` is restricted to the operations administrator policy and records a courier detail read audit entry.
- Writes are idempotent. A replay returns the original response; reuse of the same key with different request data returns a conflict.
- A courier must be ACTIVE, unsuspended, and currently offline to go online. The selected zone must be active and included in the courier's approved zones.
- An online courier must go offline before starting another online presence. Going offline clears the current zone.
- The event ledger is append-only. The current table is a projection updated only by event insertion.
- Presence does not capture GPS, coordinates, routes, or location history. It is separate from staff-planned availability windows and is not, by itself, dispatch eligibility.

## Data model

Migration `040_courier_live_presence.sql` introduces:

- `courier_live_presence_events`: append-only ONLINE/OFFLINE transition history.
- `courier_live_presence`: one current status projection per courier.
- `courier_live_presence_idempotency`: immutable request fingerprint and response snapshot for safe retries.

## Deferred

- GPS/location permissions and location samples.
- OSM maps, geocoding, route tracking, or navigation.
- Dispatch assignment and automatic eligibility based on live presence.
- Push notifications or background presence heartbeats.
- A courier mobile UI implementation; this sprint provides the secured API contract.

## Validation

PostgreSQL integration coverage exercises online/offline transitions, idempotent replay and conflict, unapproved-zone rejection, and projection mutation protection.
