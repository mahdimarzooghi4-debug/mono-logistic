# Sprint 144 — Dispatch delivery window state filter

## Goal

Let Mono operations narrow the dispatch queue to Missions whose committed delivery deadlines are upcoming, exactly due, or overdue.

## Delivered

- Add optional `delivery_window_state` filter for `upcoming`, `due_now`, and `overdue`.
- Reuse the same UTC request timestamp and state expression for response and filtering.
- Apply the filter in first-page, later-page, and cursor-validation SQL.
- Reject unsupported state values.
- Cover each state and cursor continuity/filter changes in PostgreSQL integration tests.
- Update the dispatch API contract, backlog, and README.

## Boundary

The filter is read-only; it does not alter delivery promises, Mission state, or offer actions.
