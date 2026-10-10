# Sprint 007 — Idempotent Delivery Request Intake

- Status: Draft PR / CI passing; review pending
- Phase: A — Platform Foundation
- Epic: E3 — Delivery Request & Mission / E16 — B2B API / E20 — Security
- Branch: `sprint-007-delivery-request-intake`
- Base: `sprint-006-managed-api-key-provisioning`

## Sprint goal

Accept Hana's delivery request through Mono's shared B2B API while preserving tenant isolation and safe retries.

## Scope

- Add `POST /v1/delivery-requests` protected by `delivery-requests.write`.
- Validate the Phase-1 urban delivery request shape and require an active branch owned by the authenticated customer.
- Require `Idempotency-Key`; replay the original request ID for equivalent retries and reject key reuse with changed content.
- Persist the request as `RECEIVED` with no quote, promise, or Mission implied.
- Store request data in the customer-scoped table; never accept customer identity from the body.

## Acceptance criteria

1. Unauthenticated requests receive an authentication challenge; keys lacking the required scope are forbidden.
2. Invalid body, invalid idempotency header, or inactive/foreign branch does not create a request.
3. A repeated key with equivalent normalized payload returns the original request ID.
4. A repeated key with changed payload returns `IDEMPOTENCY_CONFLICT`.
5. Same key can be used independently by a different customer.
6. New requests return `RECEIVED`; no fare, delivery promise, feasibility result, or Mission is fabricated.

## Out of scope

- Coverage decision, distance calculation, quote, promise, Mission creation, courier selection, and dispatch.
- Hana-side HTTP adapter and external status callbacks.
- Staging, deployment, or production changes.
