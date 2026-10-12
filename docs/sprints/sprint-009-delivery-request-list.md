# Sprint 009 — Customer-Scoped Delivery Request List

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E3 — Delivery Request & Mission / E16 — B2B API / E20 — Security
- Branch: `sprint-009-delivery-request-list`
- Base: `sprint-008-delivery-request-status-read`

## Sprint goal

Let an authenticated B2B customer page through its delivery request history using compact, privacy-preserving summaries.

## Scope

- Add `GET /v1/delivery-requests`, protected by `delivery-requests.read`.
- Default to 50 results per page, allow 1–100, and use deterministic keyset pagination.
- Scope both rows and cursor lookup to the authenticated customer.
- Return request ID, external reference, status, creation time, and continuation cursor only.

## Acceptance criteria

1. Keys without `delivery-requests.read` receive 403; unauthenticated calls receive 401.
2. Pages are deterministic, ordered by created time and ID descending, and do not duplicate results.
3. An invalid, unknown, or foreign-customer cursor is rejected with the same 400 response.
4. The page does not expose pickup/dropoff PII or stored payload.
5. CI builds and tests the solution and API container successfully.

## Out of scope

- Feasibility, pricing, ETA, delivery promise, Mission creation, and cancellation.
- Hana-side adapter, Stage, server deployment, and production changes.
