# Sprint 087 — Delivery Request commercial context

## Goal

Give Hana one customer-scoped response with the information needed to review a Delivery Request, its current Quote and Promise, and its Mission confirmation.

## Changes

- Extend `GET /v1/delivery-requests/{id}` with the latest Quote and current Promise.
- Return null Quote or Promise when that record has not been issued or committed.
- Reuse the existing safe read contracts and keep feasibility evidence and request payload details private.
- Add PostgreSQL-backed HTTP assertions for populated and absent commercial context.
- Update the API contract and product backlog.

## Security and scope

Every read remains scoped to the authenticated customer and Delivery Request ID. The endpoint does not change quote issuance, Promise commit, Mission confirmation, or history pagination. No Stage work or deployment is included.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and API container image build on this Draft PR.
