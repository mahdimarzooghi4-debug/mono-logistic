# Sprint 086 — Customer Delivery Request Mission context

## Goal

Let Hana retrieve confirmation and committed delivery information using the Delivery Request ID it already stores.

## Changes

- Join customer-scoped Delivery Request detail and history reads to the matching tenant-owned Mission.
- Return Mission ID, status, and committed delivery time when confirmed; return null Mission fields before confirmation.
- Keep request payload, pickup/dropoff locations, coordinates, and contact data out of the response.
- Add PostgreSQL-backed HTTP coverage for confirmed and unconfirmed requests and tenant isolation.
- Update the API contract and product backlog.

## Security and scope

The existing `delivery-requests.read` scope and customer tenant context continue to protect both detail and history. No Mission mutation, Stage work, or deployment is included.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and API container image build on this Draft PR.
