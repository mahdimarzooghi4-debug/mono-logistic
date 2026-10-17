# Sprint 201 — Shared payout reconciliation query

## Goal
Keep every payout reconciliation read surface aligned on one SQL definition of lifecycle and double-entry invariants.

## Delivered
- Extracted the common latest-withdrawal-event and payout-ledger classification CTE into one shared persistence query.
- Refactored detailed reconciliation pages, aggregate summaries, and daily trends to query that shared projection.
- Preserved each endpoint's filters, ordering, grouping, response shape, and existing read-audit behavior.
- Existing integration checks compare summary and trend totals/buckets against detailed reconciliation results.

## Validation
- The shared query remains parameterized for time range, courier, and withdrawal status.
- This is a read-model refactor; no payout posting or lifecycle behavior changes.
