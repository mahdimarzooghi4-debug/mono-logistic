# Sprint 077 — Mission timeline read

## Goal

Expose the lifecycle event envelope for a Mission created through Sprint 076.

## API

- Add `GET /v1/missions/{id}/timeline`, requiring `delivery-requests.read`.
- Return events in aggregate sequence order with event ID, type, sequence, and occurrence time.
- Return `404 MISSION_NOT_FOUND` for unknown or foreign-tenant Mission IDs.

## Data boundary

- Read the timeline from the transactional outbox for the authenticated tenant and exact Mission aggregate.
- Expose only allowlisted Mission lifecycle event types.
- Omit event payloads, contact information, and pickup/dropoff details.
- The initial timeline contains `mission.created`; other event types will appear as the matching lifecycle operations are implemented.

## Validation

PostgreSQL-backed HTTP integration coverage verifies the owner response, first event envelope, safe response boundary, and foreign-tenant 404. CI must build, run the full test suite, and build the container image.

No new lifecycle transition, handoff, cancellation, or webhook delivery behavior is introduced. No Stage or deployment is included.
