# Sprint 076 — Confirm delivery request and create Mission

## Goal

Complete the explicit B2B confirmation path from a Delivery Request, Quote, and committed Promise to the first persisted Mission.

## API

- `POST /v1/delivery-requests/{id}/confirm` requires `delivery-requests.write` and `Idempotency-Key`.
- The body identifies the Quote and the current Promise version.
- The Quote must belong to the authenticated tenant and be unexpired. The Promise must be the current version for the same tenant and request.
- A request can create one Mission. A repeated key and body replays its response; changed input conflicts.
- `GET /v1/missions/{id}` requires `delivery-requests.read`, is tenant-scoped, and returns the public Mission summary.

## Persistence and event

- Store the Mission as `PROMISED` with the confirmed Quote and Promise references.
- Persist the idempotency mapping and `mission.created` outbox event in the same PostgreSQL transaction.
- Lock the tenant-owned Delivery Request during confirmation so two concurrent confirmations cannot create duplicate Missions.

## Validation

PostgreSQL-backed HTTP integration coverage verifies successful confirmation, exact replay, changed-input conflict, expired/missing Quote, missing Promise version, tenant isolation, read/write scope enforcement, Mission read privacy, and single Mission / idempotency / outbox persistence.

No automatic confirmation contract, assignment behavior, cancellation policy, or later Mission lifecycle transition is introduced. No Stage or deployment is included.
