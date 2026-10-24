# Sprint 178 — Courier payout ledger integrity

## Goal

Make the database enforce that every manual payout settles the exact withdrawal and posts only the matching courier and Mono clearing entries.

## Contract

- Resolve every `WITHDRAWAL_PAYOUT` ledger transaction to an existing withdrawal request.
- Require the latest lifecycle event to be an operator-recorded `PAID` event with a bank reference.
- Require exactly two TOMAN ledger entries: a debit from that request's courier available account and an equal credit to Mono payout clearing.
- Keep the check deferred so both entries can be written atomically in the existing payout transaction.

## Validation

- PostgreSQL integration coverage proves a balanced but mismatched payout is rejected at commit.
- The same withdrawal remains processable after the rejected transaction rolls back, and the existing valid payout flow remains covered.
- Exact-head CI validates build, migrations, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
