# Sprint 028 — Customer Registry Search

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration
- Branch: `sprint-028-customer-registry-search`
- Base: `sprint-027-customer-registry-status-filter`

## Sprint goal

Help Mono operations locate registry entries by customer ID or the beginning of a known customer name.

## Scope

- Add an optional search value to the paginated customer directory.
- Match case-insensitive prefixes of customer ID, legal name, and display name.
- Treat SQL wildcard characters in user input literally.
- Limit search values to 1–128 trimmed characters.
- Add supporting PostgreSQL indexes and integration coverage.
- Update the operations API, backlog, and sprint documentation.

## Acceptance criteria

1. Search returns matching customer summaries and preserves status filtering.
2. Pagination remains stable when search is active.
3. Literal wildcard characters cannot broaden the search.
4. Blank and overlong values return `400 INVALID_REQUEST`.
5. The MFA-gated administrator boundary remains intact.
6. CI build, tests, and container image build pass.

## Out of scope

Fuzzy/ranked search, customer lifecycle changes, Stage, and deployment.
