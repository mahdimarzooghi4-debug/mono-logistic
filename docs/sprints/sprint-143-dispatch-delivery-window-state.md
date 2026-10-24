# Sprint 143 — Dispatch delivery window state

## Goal

Give Mono operations an explicit, consistent view of each searching Mission's committed delivery deadline.

## Delivered

- Add `delivery_window_state` to every dispatch queue item.
- Classify against one UTC timestamp captured for the request: `upcoming` before the deadline, `due_now` at the exact timestamp, and `overdue` after it.
- Preserve the existing inclusive `delivery_overdue` boolean; it remains true at the exact timestamp.
- Add PostgreSQL integration coverage for all three boundary states.
- Update the dispatch API contract, backlog, and README.

## Boundary

This field is informational and does not change Mission state, offers, or delivery promises.
