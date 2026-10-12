# Sprint 027 — Customer Registry Status Filter

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration
- Branch: `sprint-027-customer-registry-status-filter`
- Base: `sprint-026-customer-registry-list`

## Sprint goal

Help Mono operations find customers by their existing onboarding lifecycle status.

## Scope

- Add an optional `status` filter to the paginated customer directory.
- Support the existing `PENDING` and `ACTIVE` states, case-insensitively.
- Preserve the existing stable keyset ordering and cursor behavior.
- Reject other status values with `400 INVALID_REQUEST`.
- Add integration coverage and update the operations API, backlog, and sprint documentation.

## Acceptance criteria

1. The unfiltered directory continues to return customers across statuses.
2. `status=PENDING` returns pending customers only.
3. `status=ACTIVE` returns active customers only.
4. Unsupported status values return `400 INVALID_REQUEST`.
5. Pagination and the MFA-gated administrator boundary remain intact.
6. CI build, tests, and container image build pass.

## Out of scope

New customer statuses, customer lifecycle transitions, Stage, and deployment.
