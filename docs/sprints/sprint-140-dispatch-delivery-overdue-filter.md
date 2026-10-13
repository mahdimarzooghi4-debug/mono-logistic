# Sprint 140 — Dispatch delivery overdue filter

## Goal

Help Mono operations prioritize searching Missions whose committed delivery time has passed while they are still awaiting dispatch.

## Delivered

- Add a read-only `delivery_overdue` boolean to each dispatch queue item, computed against one UTC timestamp captured for the request.
- Add an optional `delivery_overdue` queue filter, consistent across first page, later pages, and cursor validation.
- Treat the committed delivery timestamp as overdue inclusively at the exact boundary.
- Add PostgreSQL integration coverage for the response flag, exact-boundary behavior, filtering, and cursor validation.
- Update the dispatch API contract, backlog, and README.

## Boundary

The signal does not change Mission state, create or expire offers, or claim that delivery has failed. It identifies searching Missions whose committed delivery time has passed.
