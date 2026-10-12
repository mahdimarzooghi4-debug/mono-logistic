# Sprint 107 — Mission Lifecycle Events

## Goal

Give Mono operations a controlled, auditable way to advance an already confirmed Mission through the execution lifecycle, with Hana as the first customer and without customer-specific core logic.

## Scope

- Add an MFA-gated operations endpoint to append one Mission lifecycle event at a time.
- Enforce the lifecycle order:
  `PROMISED → SEARCHING_COURIER → COURIER_ASSIGNED → ARRIVED_PICKUP → HANDED_TO_COURIER → PICKED_UP → IN_TRANSIT → ARRIVED_DROPOFF → DELIVERED`.
- Permit cancellation only before `HANDED_TO_COURIER`; permit a failure event while the Mission remains active.
- Persist the Mission state change, outbox event, and idempotency result in one PostgreSQL transaction.
- Attribute each event to the authenticated Mono operator and retain an optional bounded reason.
- Keep lifecycle/idempotency records append-only and preserve the existing safe timeline allowlist.

## Acceptance criteria

- A valid next transition updates the Mission and emits exactly one event with a monotonically increasing Mission sequence.
- Repeating the same key and request replays the original response; reusing a key with different input conflicts.
- Skipped, repeated, or terminal-state transitions are rejected without changing state or writing an event.
- Cancellation after handoff is rejected. Failure and cancellation rules are covered by tests.
- Only the existing Mono administrator policy (including MFA requirement) can call the route.
- PostgreSQL integration tests exercise the complete transition chain and replay/conflict/boundary cases.

## Out of scope and operational limit

This is an internal operations recorder. It does not implement courier accounts, offers, assignment selection, courier mobile actions, GPS, or cryptographic proof that physical handoff or delivery occurred. The `HANDED_TO_COURIER` event is an authorized operator assertion until those evidence-producing workflows exist. Do not treat this endpoint alone as production proof of courier custody or customer delivery.

## Dependencies

- Sprint 076 confirmed Mission creation.
- Existing Mono operations OIDC boundary and MFA-gated administrator authorization.
- Existing transactional outbox and customer-scoped delivery-request/Mission linkage.

## Validation

- Build and API test suite in CI.
- PostgreSQL repository integration test for full lifecycle, idempotent replay, changed-input conflict, invalid transition, cancellation boundary, and terminal state.
