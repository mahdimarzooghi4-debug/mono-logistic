# Sprint 190 — Courier withdrawal amount ranges

## Goal

Let couriers narrow withdrawal history by requested amount while preserving the existing date and current-status filters.

## Contract

- Add optional inclusive min_amount_toman and max_amount_toman filters.
- Reject negative bounds and a minimum greater than the maximum with INVALID_AMOUNT_RANGE.
- Apply amount bounds before pagination.
- Bind keyset cursors to normalized date, status, and amount filters; bump the cursor version so old cursors cannot be continued under changed semantics.
- Keep operations list/search behavior unchanged.

## Validation

- PostgreSQL coverage verifies inclusive exact-amount and minimum-only filtering together with the existing date window.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
