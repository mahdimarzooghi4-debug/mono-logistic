# Sprint 017 — Operations Configuration Retirement

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-017-operations-configuration-retirement`
- Base: `sprint-016-operations-configuration-authoring-api`

## Sprint goal

Allow Mono administrators to retire an active customer service configuration while preserving tenant scoping and append-only audit history.

## Scope

- Add an admin-only operation to retire an active version for an explicit customer.
- Persist the `RETIRED` status and `RETIRED` audit action atomically.
- Reject unknown, draft, and already-retired versions without changing them.
- Keep retirement distinct from deleting records; retain the configuration and its audit history.
- Document immediate effective behavior and the fail-closed outcome when no active version remains.

## Acceptance criteria

1. Unauthenticated callers receive 401 and authenticated non-administrators receive 403.
2. A Mono administrator can retire an active configuration and receive 204.
3. Unknown customer-scoped IDs return 404; non-active records return 409.
4. The configuration status and audit entry are committed in one transaction.
5. Existing API-key authentication cannot authorize retirement.
6. CI build, tests, and container image build pass.

## Out of scope

Draft editing/deletion, automatic replacement activation, Hana seed data, operations UI, Stage, and deployment.
