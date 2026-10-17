# Sprint 193 — Courier withdrawal search audit history

## Scope
- Add an administrator-only, no-store endpoint to read courier withdrawal search audit history.
- Support actor, status, courier, and audit occurrence-time filters with filter-bound keyset cursors.
- Record every page read, including empty pages, in a separate immutable audit ledger.
- Return only recorded search metadata; do not expose secrets or withdrawal destination details.

## Verification
- Embedded migration 074 in API integration tests.
- Cover filtered keyset pagination and verify each read is recorded with its actor, filters, outcome, and cursor context.
- Keep the pull request draft and unmerged for staging review.
