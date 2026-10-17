# Sprint 204 — Reconciliation audit search

## Goal
Give administrators a safe, traceable way to inspect payout reconciliation report-read history.

## Delivered
- Add an administrator-only search endpoint for report-read audit records.
- Support actor, courier, status, reconciliation-code, outcome, discrepancy-mode, and bounded occurrence-time filters.
- Use keyset pagination with cursors bound to every normalized filter.
- Record every search page, including empty results and cursor state, in a separate append-only audit ledger.
- Add migration, service registration, route mapping, and integration coverage for filtered continuation and empty results.

## Security and behavior
- Results contain report-query metadata only; payout destinations and ledger mutations remain out of scope.
- Search responses are no-store and the new audit table rejects update, delete, and truncate.
