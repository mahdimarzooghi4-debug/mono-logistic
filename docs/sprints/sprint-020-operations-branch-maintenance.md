# Sprint 020 — Mono Operations Branch Maintenance

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-020-operations-branch-maintenance`
- Base: `sprint-019-operations-branch-onboarding`

## Sprint goal

Allow Mono administrators to correct branch details through a customer-scoped, audited full-replacement operation.

## Scope

- Add an admin-only PUT route using both customer ID and branch ID in the route.
- Update only an existing ACTIVE branch that belongs to an ACTIVE customer.
- Preserve existing name, coordinate-pair/range validation and JSON-object handling.
- Treat null or omitted optional values as clearing those fields; keep branch ID and status unchanged.
- Append an UPDATED audit event with actor, reason and timestamp in the same transaction.
- Cover tenant isolation, authorization, validation rollback and audit history.

## Acceptance criteria

1. Missing/invalid tokens receive 401 and non-administrators receive 403.
2. A Mono administrator can update an active branch of an active customer.
3. A branch belonging to another customer returns 404.
4. Invalid data returns 400 and leaves the branch and audit unchanged.
5. Audit history contains CREATED and UPDATED events with the expected actor/reason.
6. CI build, tests, and container image build pass.

## Out of scope

Branch deactivation/deletion, zone creation, operating-hours policy schema, Hana-specific data, Stage, and deployment.
