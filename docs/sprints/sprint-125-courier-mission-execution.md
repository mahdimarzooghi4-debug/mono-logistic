# Sprint 125 — Courier mission execution

## Goal

Connect accepted courier offers to authenticated execution milestones on the Mission lifecycle.

## Delivered

- Add `POST /courier/v1/missions/{missionId}/events` under courier authorization.
- Permit the authenticated courier to report pickup arrival, handoff, pickup, in-transit, drop-off arrival, and delivery.
- Verify the Mission has an accepted offer assigned to that courier; other couriers receive the same not-found response as an unassigned Mission.
- Preserve idempotent lifecycle events and append them to the existing Mission outbox.
- Keep courier transitions from invoking operations-only assignment, search, cancellation, or failure events.
- Add PostgreSQL integration coverage from offer acceptance through delivery, including wrong-courier denial and replay.

## Boundary

No GPS, geofencing, map, OSM, route calculation, proof-of-delivery attachment, or automatic reassignment. Those need separate product and technical contracts.
