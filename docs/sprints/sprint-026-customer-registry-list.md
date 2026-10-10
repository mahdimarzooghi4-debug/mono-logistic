# Sprint 026 — Customer Registry Listing

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration
- Branch: `sprint-026-customer-registry-list`
- Base: `sprint-025-api-key-audit-inspection`

## Sprint goal

Let Mono administrators find and page through customer registry records using the existing customer lifecycle data.

## Scope

- Add an MFA-gated operations endpoint to list customer summaries.
- Use keyset pagination ordered by creation time and customer ID.
- Default to 50 records per page and accept page sizes from 1 to 100.
- Reject malformed and unknown cursors with `INVALID_CURSOR`.
- Add an index for the listing order and integration coverage for pagination and access control.
- Document the operations API and backlog capability.

## Acceptance criteria

1. Unauthenticated requests receive 401 and non-administrators receive 403.
2. Administrators receive summary-only customer records in stable order.
3. A next cursor retrieves the next page without repeating the previous page's customer.
4. Invalid page sizes return `400 INVALID_REQUEST`; invalid cursors return `400 INVALID_CURSOR`.
5. The list does not expose secrets or audit data and does not mutate customer records.
6. CI build, tests, and container image build pass.

## Out of scope

Customer creation or lifecycle changes, customer self-service, Stage, and deployment.
