# Sprint 127 — Pre-handoff courier reassignment

## Goal

Allow Mono operations administrators to release an accepted courier assignment and return the mission to the dispatch queue while the courier has not taken custody.

## Contract

- Only an authenticated Mono administrator can release an assignment.
- The mission and offer IDs must refer to the same mission.
- The offer must be accepted and the mission must be `COURIER_ASSIGNED` or `ARRIVED_PICKUP`.
- Release is rejected at or after `HANDED_TO_COURIER`.
- The accepted offer is closed with an append-only `RELEASED` event using the fixed reason `OPERATIONS_REASSIGNED_COURIER`.
- The mission returns to `SEARCHING_COURIER`; a `mission.courier_unassigned` outbox event records the old courier and offer.
- The offer update, mission status, outbox event, and idempotency snapshot commit atomically.
- Operations can create a replacement offer after release. Existing issue reports remain unchanged.

## API

`POST /operations/v1/missions/{missionId}/courier-offers/{offerId}/release-assignment`

Requires the operations administrator authorization policy and exactly one valid `Idempotency-Key` header. Success returns the released offer. Replaying the same key returns its stored result; using that key with a different request returns an idempotency conflict.

## Verification

Integration coverage exercises release, replay, replacement assignment, and rejection after handoff. The migration adds the release event and outbox types while retaining event-only offer projections.
