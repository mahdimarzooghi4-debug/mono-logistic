# Sprint 120 — Courier mission offers

## Goal

Close the gap between operations identifying a live courier candidate and a courier accepting a real Mission, while preserving an explicit human offer and courier consent.

## Flow

1. Operations creates one offer for a `SEARCHING_COURIER` Mission, a named courier, and an active zone.
2. Mono verifies that the courier is ACTIVE, unsuspended, currently ONLINE in that zone, and approved for the zone.
3. The authenticated courier can read their pending offer and accept or decline it. The courier ID comes only from the validated Mono courier context.
4. Acceptance records the offer event, moves the Mission to `COURIER_ASSIGNED`, and appends `mission.courier_assigned` to the transactional outbox in one database transaction.
5. Decline leaves the Mission searching so operations can make another offer. A terminal Mission event closes an outstanding/accepted offer as CANCELLED or COMPLETED.
6. Idempotency keys protect offer creation and courier responses. Offer events and idempotency records are append-only; the offer status row is event-maintained.

## Endpoints

- `POST /operations/v1/missions/{missionId}/courier-offers`
- `GET /courier/v1/mission-offers`
- `POST /courier/v1/mission-offers/{offerId}/accept`
- `POST /courier/v1/mission-offers/{offerId}/decline`

## Constraints

- At most one pending/accepted offer exists per Mission and per courier.
- An offer is scoped to the operations-selected active zone; there is no inferred mission zone or automatic geographic dispatch.
- This is not GPS tracking, routing, navigation, or OSM integration. Those remain deferred as previously decided.
- Mission delivery milestones remain governed by the existing Mission lifecycle. This sprint records acceptance only; it does not fabricate pickup/delivery confirmations.
- The offer inbox returns only the courier's own offer and minimal Mission identifiers; customer contact and request payload are not exposed.

## Validation

PostgreSQL integration coverage verifies offer creation/replay/conflict, authenticated courier inbox content, acceptance/replay, atomic Mission status and outbox assignment, and cancellation closure.
