# Sprint 207 — Courier issue inbox search

## Goal
Give Operations a cross-Mission inbox for finding and prioritizing structured courier issue reports.

## Delivered
- Add an administrator-only, no-store inbox at `GET /operations/v1/courier-issues`.
- Filter by status, issue code, Mission, courier, and reported-time window.
- Use stable keyset pagination ordered by report time and report ID.
- Bind cursors to normalized filters and reject malformed or reused-under-different-filters cursors.
- Record each inbox page, including empty results, in the existing append-only Mission operations read audit.
- Add a migration, dependency registration, and backlog traceability.

## Security and behavior
- Return only structured report metadata; no courier notes or location data are exposed.
- Existing per-Mission read and resolve endpoints remain unchanged.
- This read capability does not change Mission status, assignments, or exception resolution rules.
