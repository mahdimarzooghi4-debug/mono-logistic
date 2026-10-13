# Sprint 145 — Dispatch due horizon

## Goal

Let Mono operations find searching Missions whose committed delivery deadline is approaching within a chosen short horizon.

## Delivered

- Add optional `delivery_due_within_minutes`, from 1 to 10,080 minutes.
- Include deadlines from the request's UTC timestamp through the computed endpoint, inclusive.
- Exclude deadlines that have already passed.
- Apply the same request-level horizon in cursor validation, first-page, and later-page queries.
- Cover inclusive boundaries, range validation, and cursor/filter consistency in PostgreSQL tests.
- Update API documentation, backlog, and README.

## Boundary

The filter is read-only and does not automatically change Mission or offer state. Its relative time window is recalculated for each request.
