# Sprint 206 — Route plan read audit

## Goal
Make administrator inspection of current and historical Route plan versions traceable.

## Delivered
- Record every valid current-plan and historical-version read, including not-found reads.
- Add an administrator-only, time-bounded audit search with route, action, and outcome filters.
- Use stable keyset pagination with cursors bound to all normalized filters.
- Record reads of the audit history itself in the same append-only ledger.
- Add migration, API registration, and PostgreSQL integration coverage.

## Security and behavior
- The audit ledger rejects update, delete, and truncate.
- Invalid route/version requests are rejected before the database read and do not disclose route data.
- This change adds accountability to the existing route-plan inspection boundary; it does not change planning or dispatch decisions.
