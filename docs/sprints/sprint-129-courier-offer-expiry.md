# Sprint 129 — Courier offer expiry

## Goal

Make unanswered offers visible with a deadline and let Mono operations close them once they expire. This clears the active courier slot so the dispatch queue can proceed safely.

## Behavior

- Newly created offers receive a five-minute response window.
- The deadline is returned by offer APIs and shown in the operations dispatch queue. The queue exposes `active_offer.deadline_passed` so operations can identify when the explicit expiry action is available.
- Only an unanswered `OFFERED` record on a `SEARCHING_COURIER` mission can expire.
- Expiry before the deadline, after courier acceptance, or after the mission leaves the dispatch queue is rejected.
- An accepted expiry appends an `EXPIRED` event with fixed reason `OPERATIONS_EXPIRED_OFFER`; it never edits prior offer history.
- The mission remains `SEARCHING_COURIER`. Operations can then issue a replacement offer.
- Expiry is idempotent using `Idempotency-Key`.

## API

`POST /operations/v1/missions/{missionId}/courier-offers/{offerId}/expire`

Requires the operations administrator authorization policy and one valid `Idempotency-Key` header. Operations must invoke it after the displayed deadline. This sprint does not add a scheduled worker; cleanup remains an explicit operations action.

## Verification

The PostgreSQL integration test verifies the deadline in the queue result, rejects an early expiry, advances a controlled clock past the deadline, expires the offer, and confirms replay returns the same result.
