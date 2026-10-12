# Sprint 113 — Planned zone capacity projection

## Goal

Turn staff-entered courier windows into a bounded, auditable view of planned capacity by zone and time.

## Scope

- Add a Mono administrator endpoint to project one active zone across a UTC time range.
- Support 15, 30, and 60 minute buckets aligned to the requested range.
- Count distinct ACTIVE couriers with scheduled windows overlapping each bucket.
- Bound each request to 24 hours, producing at most 96 result buckets.
- Record projections in the append-only courier operations read-audit ledger and fail closed on audit write errors.

## Acceptance criteria

- Invalid or inactive zones and misaligned, unsupported, or overlong ranges are rejected.
- Windows count in every bucket they overlap; gaps return zero.
- Only ACTIVE couriers with SCHEDULED windows contribute to counts.
- Results include bucket boundaries and planned courier count, never courier identity or schedule detail.
- The projection audit event records actor, action, number of output buckets, and timestamp, without the requested filter values.
- PostgreSQL integration coverage verifies overlap counting, empty buckets, output bounds, and audit entry.

## Data and operational boundary

The projection contains aggregate planning counts only. It does not represent real-time supply, GPS/location, route capacity, or dispatch eligibility. OSM/GPS, Stage, and deployment remain deferred.

## Validation

- PostgreSQL integration tests use the isolated courier schema and migrations 033–036.
- Full CI runs .NET build/tests and container image build.
