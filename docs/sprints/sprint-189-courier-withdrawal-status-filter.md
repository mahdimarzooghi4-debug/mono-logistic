# Sprint 189 — Courier withdrawal status filters

## Goal

Let couriers narrow withdrawal history to a lifecycle state while preserving the date-window contract introduced in Sprint 188.

## Contract

- Add an optional exact current-status filter to the courier withdrawal history endpoint.
- Accept REQUESTED, PROCESSING, PAID, and REJECTED case-insensitively after trimming whitespace.
- Return INVALID_STATUS for any other supplied value.
- Apply status and date filters before pagination.
- Bind keyset cursors to the normalized status and date window; bump the cursor version so old cursors cannot be reused with changed semantics.
- Keep operations list/search behavior unchanged.

## Validation

- PostgreSQL coverage checks status filtering inside a date window for requested and rejected withdrawals.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
