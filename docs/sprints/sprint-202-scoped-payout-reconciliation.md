# Sprint 202 — Scoped payout reconciliation

## Goal
Keep reconciliation reads efficient as payout and ledger history grows.

## Delivered
- Restrict candidate withdrawals by the requested time range, optional courier, and optional status before evaluating payout ledger rows.
- Resolve each candidate's latest lifecycle action with the existing withdrawal/sequence index instead of scanning all event history.
- Join payout transactions by withdrawal reference and add a partial index for WITHDRAWAL_PAYOUT references.
- Preserve the shared reconciliation classification and the report, summary, and trend contracts.
- Add migration coverage and assert that the payout-reference index is present in the integration database.

## Validation
- Existing integration coverage continues to cross-check detailed report, summary, and trend values.
- This read-path optimization does not change payout posting or withdrawal lifecycle behavior.
