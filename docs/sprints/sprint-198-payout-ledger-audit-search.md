# Sprint 198 — Payout ledger detail audit search

## Goal
Give administrators a safe way to inspect the append-only read history created by payout ledger drilldowns.

## Delivered
- Added GET /operations/v1/courier-payouts/ledger-detail-audit with administrator authorization and no-store responses.
- Supports actor, withdrawal, outcome, and bounded occurrence-time filters.
- Uses descending keyset pagination with cursors bound to all normalized filters.
- Records every valid search page in a separate append-only audit table, including empty pages and cursor state.
- Added integration coverage for two-page continuation and page-read audit persistence.

## Security
- Search results include only read audit metadata and transaction/entry counts.
- Read/search audit tables reject update, delete, and truncate mutations.
