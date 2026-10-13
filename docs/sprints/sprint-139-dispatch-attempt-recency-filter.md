# Sprint 139 — Dispatch attempt recency filter

## Goal

Help operations identify searching Missions whose most recent courier-offer activity has remained unanswered beyond a chosen time.

## Delivered

- Add the optional `last_attempt_before` filter to the administrator-only dispatch queue.
- Compare against the latest recorded courier-offer event timestamp for the Mission.
- Use an inclusive UTC ISO 8601 cutoff and exclude Missions with no offer history when the filter is present.
- Apply the filter to initial pages, subsequent pages, and cursor validation.
- Add PostgreSQL integration coverage for no-attempt exclusion and exact timestamp boundaries.
- Update the API contract, backlog, and README.

## Boundary

The filter is read-only. It does not order the queue, expire offers, reassign couriers, or change Mission state.
