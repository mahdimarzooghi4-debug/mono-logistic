# Sprint 085 — Delivery Request read-audit inspection

## Goal

Give Mono administrators a filterable history of privileged Delivery Request reads, including who searched or opened a request and how many results were returned.

## Changes

- Add MFA-gated keyset-paginated inspection of the Delivery Request operations read ledger.
- Filter by Delivery Request ID, staff actor, and inclusive occurrence-time bounds.
- Reject cursors that do not match every active filter.
- Record each ledger inspection in the same append-only audit ledger.
- Extend the audit schema for the inspection action.
- Add PostgreSQL coverage for filtering, paging, audit self-recording, authorization, and immutability.

## Security and scope

Only the existing MFA-verified Mono administrator policy can inspect this ledger. Search terms and filter values are not persisted. This package is read-only and does not change Delivery Request or Mission state. No Stage work or deployment is included.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and API container image build on this Draft PR.
