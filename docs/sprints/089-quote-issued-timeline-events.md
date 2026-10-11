# Sprint 089 — Quote issuance timeline events

## Goal

Complete the recorded customer lifecycle timeline from Quote issuance through Promise commitment and Mission confirmation.

## Changes

- Emit `quote.issued` in the same PostgreSQL transaction as each newly persisted Quote version.
- Preserve idempotency: replayed or conflicting Quote writes do not add another event.
- Add an idempotent migration that expands the outbox event allowlist and backfills existing Quote versions.
- Include the allowlisted Quote issuance envelope in the tenant-scoped Delivery Request timeline.
- Keep event payloads out of customer API responses.
- Add PostgreSQL-backed coverage for persistence, retry deduplication, chronology, tenant isolation, and payload exclusion.
- Update the API contract and product backlog.

## Security and scope

The outbox event contains only Quote ID, Delivery Request ID, and version. Timeline reads return the event envelope only, filtered through tenant-owned Quote, Promise, and Mission aggregates. No pricing evidence or request payload is exposed. No Stage work or deployment is included.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and API container image build on this Draft PR.
