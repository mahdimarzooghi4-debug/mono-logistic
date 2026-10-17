# Sprint 199 — Payout reconciliation summary

## Goal
Provide operations dashboards with an aggregate view of payout reconciliation coverage and discrepancy value.

## Delivered
- Added GET /operations/v1/courier-payouts/reconciliation/summary for administrator callers.
- Aggregates withdrawal count and toman value by reconciliation code, plus total and discrepancy counts.
- Supports bounded UTC creation-time range and optional courier and status filters.
- Records every aggregate read and its filters/results in append-only audit history.
- Added integration coverage comparing summary totals and buckets to the paginated reconciliation detail results.

## Security and behavior
- Does not expose withdrawal-level or payout-destination data.
- Returns no-store responses and uses the same double-entry reconciliation invariants as the detailed report.
- Read-only; no payout status or ledger entries are changed.
