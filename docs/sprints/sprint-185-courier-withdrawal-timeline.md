# Sprint 185 — Courier withdrawal timeline

## Goal

Let each courier inspect the lifecycle of their own withdrawal without exposing operational actor identities or internal idempotency metadata.

## Contract

- Add an authenticated courier read at `GET /courier/v1/finance/withdrawals/{withdrawalId}/history`.
- Scope the query to the authenticated courier and return the same not-found response for missing and other-courier withdrawal IDs.
- Return only action, event time, rejection reason code, and bank reference; omit actor IDs and idempotency keys.
- Mark the response `Cache-Control: no-store`; keep the existing audited operations history endpoint unchanged.

## Validation

- PostgreSQL integration coverage verifies lifecycle ordering, safe event fields, and cross-courier denial.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
