# Sprint 021 — Mono Operations Branch Inspection

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-021-operations-branch-inspection`
- Base: `sprint-020-operations-branch-maintenance`

## Sprint goal

Give authenticated Mono administrators a read-only view of branches belonging to one selected customer.

## Scope

- Add `GET /operations/v1/customers/{customerId}/branches`.
- Require the existing Mono administrator policy.
- Validate the customer ID and return 404 for an unknown customer.
- Return branch summary fields, including status, sorted by name and branch ID.
- Return an empty list for a known customer without branches.
- Keep the existing customer-facing `GET /v1/branches` route and tenant boundary unchanged.
- Cover authorization, customer scope, response fields, and unknown-customer behavior.

## Acceptance criteria

1. Missing credentials receive 401 and non-administrators receive 403.
2. A Mono administrator sees only the selected customer's branches.
3. Branch summaries include ID, name, address, coordinates, zone, and status.
4. A known customer without branches receives an empty list; an unknown customer returns 404.
5. The read operation does not mutate branch data or append audit events.
6. CI build, tests, and container image build pass.

## Out of scope

Customer-facing API changes, pagination, branch mutation, Hana-specific records, Stage, and deployment.
