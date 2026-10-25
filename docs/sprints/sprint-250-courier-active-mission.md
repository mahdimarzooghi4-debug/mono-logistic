# Sprint 250 — Courier active mission execution

## Goal

Let the courier resume an accepted assignment, open the device's map app for either stop, and report the lifecycle milestones already supported by the API.

## Delivered

- Add `GET /courier/v1/missions/active` with `no-store`, courier authorization, and privacy-limited mission/status/coordinate fields.
- Resolve points only for an accepted offer owned by the authenticated courier and an active mission state.
- Restore active mission state after acceptance or page reload.
- Add explicit, manual lifecycle event actions using existing `POST /courier/v1/missions/{missionId}/events` and idempotency keys.
- Open the installed mobile mapping app using `geo:` links when coordinates are valid; no route engine, ETA, GPS arrival inference, address, or contact data is added.
- Add DTO privacy/state tests and mobile acceptance-to-arrival-pickup coverage.

## Verification

Run API tests and `npm test && npm run build` in `src/Mono.CourierMobile`. Keep the PR Draft and unmerged; Stage configuration remains external.
