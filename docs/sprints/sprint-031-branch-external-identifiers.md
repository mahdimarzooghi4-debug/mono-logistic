# Sprint 031 — Branch External Identifiers

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration
- Branch: `sprint-031-branch-external-identifiers`
- Base: `sprint-030-branch-detail-audit-inspection`

## Sprint goal

Associate external-system branch IDs with Mono branches so B2B customers can maintain a stable mapping without introducing customer-specific branch fields.

## Scope

- Add and list external identifiers through MFA-gated Mono operations routes.
- Normalize namespaces and preserve external ID case.
- Enforce one active namespace/external ID mapping per branch-owning customer.
- Retire mappings rather than deleting them and preserve append-only audit history.
- Allow a retired pair to be assigned to another branch within the same customer.
- Add PostgreSQL constraints, integration coverage, and API documentation.

## Acceptance criteria

1. Unauthenticated and non-administrator requests are rejected.
2. Administrators can add and list identifiers for a branch owned by the specified customer.
3. An active namespace/external ID pair cannot map to multiple branches in the same customer.
4. Customer and branch ownership are enforced by the API and database.
5. Retirement and its audit record are committed atomically; the audit table is immutable.
6. Retired mappings can be reassigned while prior history remains.
7. CI build, tests, and container image build pass.

## Out of scope

External-system synchronization or lookup from customer APIs, branch lifecycle changes, Stage, and deployment.
