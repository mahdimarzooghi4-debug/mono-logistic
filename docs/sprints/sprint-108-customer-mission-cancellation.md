# Sprint 108 — Customer Mission cancellation

## Goal

Allow a Mono customer such as Hana to cancel a confirmed Mission before physical handoff through the B2B API.

## Scope

- Implement `POST /v1/missions/{mission_id}/cancel`.
- Require the dedicated `missions.cancel` API-key scope and exactly one valid `Idempotency-Key`.
- Accept the `CUSTOMER_CANCELLED` reason code and an optional bounded external reference.
- Scope Mission lookup and row locking to the authenticated customer tenant.
- Reuse the Mission lifecycle transaction: update status, append `mission.cancelled` to the outbox, and save the idempotency response atomically.
- Serialize cancellation against operations handoff transitions using the Mission row lock.
- Return `404 MISSION_NOT_FOUND` for missing and foreign-tenant Mission IDs; return a structured `409 CANCELLATION_NOT_ALLOWED` after handoff or for a terminal Mission.
- Keep the customer response to Mission ID and status; keep event payload and reason details out of the customer timeline response.

## Acceptance criteria

- Exact retry with the same key/request replays the same success response.
- Reusing the same key with changed request data returns `409 IDEMPOTENCY_CONFLICT`.
- A foreign tenant cannot discover or cancel another tenant's Mission.
- API keys without `missions.cancel` are denied.
- Cancellation before handoff succeeds and produces exactly one cancellation event.
- Concurrent distinct-key cancellation requests serialize: one can cancel, and the other observes the terminal state without writing another event.
- Once handoff or a terminal state is recorded, cancellation is rejected without changing state or adding an event.

## Dependencies

- Sprint 107 Mission lifecycle migration, ordered transition service, and append-only idempotency/outbox support.
- Existing customer API-key tenant context and authorization policies.

## Operational boundary

This Sprint implements the contract's pre-handoff rule. Policy configurability is not added. An operator-entered handoff remains an operational assertion until courier identity and physical proof capabilities are built.

## Validation

- Full .NET build and tests with PostgreSQL-backed endpoint coverage.
- API contract and product backlog updated.
