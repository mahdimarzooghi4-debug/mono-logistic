# Sprint 078 — Customer Mission history

## Goal

Let a customer retrieve the Missions it has confirmed, with stable pagination and the same safe summary boundary as GET /v1/missions/{id}.

## API

- Add GET /v1/missions?page_size=50&cursor=mis_...
- Require delivery-requests.read.
- Default page size is 50; accept 1–100.
- Order by created_at DESC, mission_id DESC; use keyset pagination.
- Return 400 INVALID_CURSOR for malformed, unknown, or foreign-customer cursors.

## Data boundary

- Scope list and cursor lookup by the authenticated customer.
- Reuse the public Mission summary fields.
- Do not return pickup/dropoff, contacts, or internal evidence.
- Use the existing (customer_id, created_at DESC, mission_id DESC) index.

## Validation

PostgreSQL-backed HTTP integration tests verify two-page traversal, no repeated Mission IDs, terminal cursor behavior, tenant isolation, foreign cursor rejection, invalid page size, and privacy of the response.

No lifecycle transitions, cancellation policy, or Stage/deployment behavior is introduced.
