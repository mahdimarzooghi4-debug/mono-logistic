# Sprint 181 — Reconciled courier ledger balances

## Goal

Complete the courier's period statement with posted balances before and after the selected interval.

## Contract

- Return the opening posted balance from ledger entries strictly before `from_at`.
- Return the closing posted balance from ledger entries strictly before the exclusive `to_at`.
- Keep totals and opening/closing values consistent across pages and under the same repeatable-read snapshot.
- Label the balances as posted ledger values; pending withdrawal reservations remain outside the ledger statement.

## Validation

- PostgreSQL coverage verifies zero opening balance for a full-history interval, a non-zero opening balance for a payout-only interval, and the matching closing balance after payout.
- Verify statement balances and movement totals remain identical across keyset pages.
- Exact-head CI validates build, migration checks, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
