# Sprint 195 — Global payout destination audit search

## Scope
- Add an administrator-only, no-store search across payout destination verification, revocation, and secret-access events.
- Filter by courier, event type, action, actor, and occurrence-time window.
- Bind stable keyset cursors to all active filters.
- Record every page read in its own append-only audit ledger.
- Return only masked account details and safe evidence metadata.

## Verification
- Embed migration 076 in PostgreSQL integration tests.
- Cover filtered pagination and verify both pages are recorded, including cursor context.
- Keep the pull request draft and unmerged for staging review.
