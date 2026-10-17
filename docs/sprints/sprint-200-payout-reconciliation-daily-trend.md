# Sprint 200 — Payout reconciliation daily trend

## Goal
Give operations dashboards a day-by-day UTC view of reconciliation coverage and discrepancy value.

## Delivered
- Added GET /operations/v1/courier-payouts/reconciliation/daily-trend with administrator authorization.
- Groups withdrawals by UTC creation date and reconciliation code, reporting counts and toman amounts.
- Supports optional courier/status filters and a required half-open UTC creation-time range.
- Records filter and aggregate result metadata in append-only audit history.
- Added integration coverage that compares trend buckets against the detailed reconciliation report.

## Security and behavior
- Returns no-store responses and does not expose individual withdrawal or payout-destination data.
- Uses the same payout ledger matching invariants as the reconciliation report and summary.
- Read-only; payout and ledger state are unchanged.
