# Sprint 008 — Customer-Scoped Delivery Request Status Read

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E3 — Delivery Request & Mission / E16 — B2B API / E20 — Security
- Branch: `sprint-008-delivery-request-status-read`
- Base: `sprint-007-delivery-request-intake`

## Sprint goal

Let an authenticated B2B customer retrieve the receipt status for one of its own delivery requests.

## Scope

- Add `GET /v1/delivery-requests/{delivery_request_id}` protected by `delivery-requests.read`.
- Return the request ID, external reference, current status, and creation timestamp.
- Resolve the request only within the authenticated customer's tenant.
- Return the same not-found response for unknown IDs and IDs belonging to other customers.
- Keep the response at `RECEIVED`; do not claim feasibility, quote, ETA, promise, or Mission state.

## Acceptance criteria

1. Keys without `delivery-requests.read` receive 403; unauthenticated calls receive 401.
2. A customer can read its own request summary.
3. A different customer cannot discover or read the request.
4. Unknown and foreign-customer IDs both return 404.
5. The endpoint does not return pickup/dropoff PII or the stored request payload.
6. CI builds and tests the solution and API container successfully.

## Out of scope

- Feasibility, pricing, ETA, delivery promise, Mission creation, and cancellation.
- Hana-side adapter, Stage, server deployment, and production changes.
