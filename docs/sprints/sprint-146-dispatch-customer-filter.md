# Sprint 146 — Dispatch customer filter

## Goal

Let Mono operations focus the dispatch queue on one customer while preserving the existing cross-customer queue when no filter is supplied.

## Change

- Add optional `customer_id` to `GET /operations/v1/dispatch/queue`.
- Accept only the canonical `cus_` plus 32 lowercase hexadecimal character ID format.
- Apply exact customer equality to cursor validation, the first page, and subsequent pages.
- Keep the filter optional so the existing Mono-wide operations view remains available.
- Document the parameter and its pagination behavior.

## Acceptance

- A filtered queue contains only Missions belonging to the requested customer.
- Omitting the filter preserves the existing queue result set.
- A cursor from another customer scope is rejected.
- Existing queue filters and ordering continue to work.

## Verification

- Extend PostgreSQL-backed dispatch queue integration coverage with two customers, one Mission per customer, and a cross-scope cursor check.
- GitHub Actions runs the repository test and build gates on this draft change.

## Out of scope

- Customer-specific authorization roles or credentials.
- Automatic dispatch or Mission state changes.
- Stage deployment.
