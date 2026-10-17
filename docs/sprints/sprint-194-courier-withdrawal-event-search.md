# Sprint 194 — Courier withdrawal lifecycle event search

## Scope
- Add an administrator-only endpoint to search lifecycle events across courier withdrawals.
- Filter by action, actor type and ID, courier, and event occurrence window.
- Use stable keyset pagination with cursors bound to every normalized filter.
- Record every page read, including empty pages, in a separate append-only audit table.
- Expose safe operational event fields without idempotency keys or request hashes.

## Verification
- Embed migration 075 in API integration tests.
- Cover filtered event pagination and verify the read ledger captures both pages and cursor use.
- Keep the pull request draft and unmerged for staging review.
