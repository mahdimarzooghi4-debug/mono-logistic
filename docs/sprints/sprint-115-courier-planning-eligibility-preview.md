# Sprint 115 — Explainable courier planning eligibility

## Goal

Show Mono operators which couriers contribute to planned capacity in a selected active zone and time range, and the exact operational reasons others are excluded.

## Product story

As a Mono operations administrator, I need a bounded, auditable per-courier explanation behind a planned-capacity view so I can review supply and correct planning inputs before dispatch capabilities exist.

## Scope

- Add an MFA-gated, read-only planning eligibility endpoint.
- Evaluate trust status, operational suspension, zone approval, and overlapping scheduled availability.
- Return stable exclusion reason codes and an explicit `PLANNED_CAPACITY_ONLY` classification.
- Use keyset pagination bounded to 100 couriers per page and a UTC range of at most 24 hours.
- Record reads in the append-only courier operations audit ledger with a dedicated action.
- Preserve failure-closed audit behavior.

## Acceptance criteria

- A courier contributes only when trust status is ACTIVE, operational status is not SUSPENDED, the requested zone is approved, and a SCHEDULED window overlaps the requested range.
- Exclusion reasons identify every failed planning condition: `COURIER_NOT_ACTIVE`, `COURIER_SUSPENDED`, `ZONE_NOT_APPROVED`, and `NO_SCHEDULED_WINDOW`.
- Search is ordered by Courier ID; invalid/missing cursors fail as a client error and do not silently restart or skip arbitrarily.
- Range, timezone, page-size, zone, and cursor validation are enforced in the service.
- Every successful response has `classification = PLANNED_CAPACITY_ONLY`.
- Read-audit rows are append-only and use `COURIER_PLANNING_ELIGIBILITY_SEARCH`.
- PostgreSQL integration tests cover included, inactive, suspended, and no-window courier records, pagination, cursor rejection, and audit persistence.

## Boundaries

This endpoint does not create or accept courier offers, assign Missions, authorize dispatch, or claim live availability. It does not use GPS, OSM routing, route capacity, current Mission load, courier authentication, or acceptance state. Those capabilities remain prerequisites for real assignment.

No Stage or production deployment is included.

## Validation

The exact branch head must pass the repository's .NET build, PostgreSQL-backed tests, and container-image CI job.
