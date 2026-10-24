# Sprint 203 — Reconciliation code filter

## Goal
Let operations investigate one payout reconciliation outcome at a time without weakening paging or audit traceability.

## Delivered
- Add an optional administrator report filter for MATCHED, MISSING_PAYOUT, UNEXPECTED_PAYOUT, MULTIPLE_PAYOUTS, or LEDGER_MISMATCH.
- Bind the normalized code into keyset cursors so a cursor cannot be replayed under a different classification filter.
- Persist the selected code in the append-only read audit, including empty result pages.
- Add database validation for the audit filter and integration coverage for matching, empty, paginated, and audited reads.

## Validation
- Existing unfiltered and discrepancy-only behavior remains covered.
- The report remains read-only; payout posting and lifecycle state are unchanged.
