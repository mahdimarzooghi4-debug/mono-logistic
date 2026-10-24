# Sprint 196 — Courier payout reconciliation

## Scope
- Add a read-only, administrator-only reconciliation report over a required creation-time window.
- Compare each withdrawal's latest lifecycle action with payout transaction count and its two expected double-entry ledger lines.
- Identify missing, unexpected, duplicate, or mismatched payout postings; never repair or mutate ledger data.
- Support courier/status filters, discrepancy-only mode, filter-bound keyset pagination, and page-read auditing.

## Verification
- Embed migration 077 and verify a paid withdrawal reconciles to exactly one balanced payout posting.
- Exercise keyset continuation and confirm each page is audited with its filter and cursor.
- Keep the pull request draft and unmerged for staging review.
