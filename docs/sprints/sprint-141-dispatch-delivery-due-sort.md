# Sprint 141 — Dispatch delivery due sorting

## Goal

Let Mono operations prioritize searching Missions with the nearest committed delivery time.

## Delivered

- Add optional `sort_by=delivery_due` to the dispatch queue API.
- Order by committed delivery time ascending, then Mission creation time descending, then Mission ID descending.
- Preserve newest-created-first ordering when omitted or set to `created_at`.
- Keep keyset pagination aligned with the selected ordering and cursor boundary values.
- Cover differing due/created order and next-page behavior in PostgreSQL integration tests.
- Update the dispatch API contract, backlog, and README.

## Boundary

Sorting is an operational view only. It does not change Mission state, dispatch decisions, or delivery promises. Keep sort and filters fixed when continuing through a cursor.
