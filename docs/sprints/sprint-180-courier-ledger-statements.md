# Sprint 180 — Courier ledger statements

## Goal

Give couriers a finance statement for a requested period, with stable pagination and consistent period totals.

## Contract

- Extend the existing courier ledger read with optional paired `from_at` and exclusive `to_at` bounds.
- Preserve the unbounded history behavior when both bounds are omitted.
- Return total debit and credit amounts for the complete selected period on every page.
- Run page and totals queries under one repeatable-read snapshot.
- Bind continuation cursors to the exact normalized period to prevent cross-period pagination.
- Add a ledger transaction time index for bounded history and summary queries.

## Validation

- PostgreSQL integration coverage verifies filtered results, exclusive upper bound, period totals, and stable totals across cursor pages.
- Existing unbounded history and payout/earning entries remain covered.
- Exact-head CI validates build, migration checks, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
