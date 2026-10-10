# Sprint 030 — Branch Detail and Audit Inspection

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration
- Branch: `sprint-030-branch-detail-audit-inspection`
- Base: `sprint-029-customer-external-identifiers`

## Sprint goal

Allow Mono operations administrators to inspect a branch's full current record and its immutable change history within the owning customer boundary.

## Scope

- Add an MFA-gated, read-only branch detail endpoint, including operating-hours JSON.
- Add chronological branch audit history with keyset pagination.
- Keep customer and branch ownership in every query; return the same not-found result for unknown and cross-customer branch IDs.
- Add integration coverage and update operations API/backlog documentation.

## Acceptance criteria

1. Requests without the Mono administrator MFA policy are rejected.
2. Administrators can inspect full branch details for the specified customer.
3. Unknown or cross-customer branch IDs return `404 BRANCH_NOT_FOUND`.
4. Audit history is chronological and paginated with a branch-scoped cursor.
5. Invalid page sizes and cursors fail with `400` without exposing data.
6. CI build, tests, and container image build pass.

## Out of scope

Branch lifecycle changes, audit mutation, Stage, and deployment.
