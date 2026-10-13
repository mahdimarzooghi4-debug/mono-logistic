# Sprint 124 — Courier offer event history

## Goal

Give Mono operations a complete, attributable view of how a courier offer reached its current status.

## Delivered

- Add `GET /operations/v1/missions/{missionId}/courier-offers/{offerId}/events`.
- Return the append-only event stream in sequence order, including action, actor, reason, and timestamp.
- Require administrator authorization and verify the offer belongs to the Mission in the route.
- Record successful reads as `MISSION_COURIER_OFFER_HISTORY` in the append-only Mission operations read-audit ledger before returning the history.
- Add integration coverage for offered/withdrawn history and the corresponding read-audit entry.

## Boundary

The route does not expose customer data, alter event history, or expand courier/customer access. No Stage, deployment, GPS, or OSM work is included.
