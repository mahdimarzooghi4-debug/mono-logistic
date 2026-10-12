# Sprint 012 — Internal Configuration Inspection

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-012-service-configuration-inspection`
- Base: `sprint-011-service-configuration-authoring`

## Sprint goal

Give authorized Mono operations staff a bounded read-only view of customer service configuration versions and their audit history.

## Scope

- List version metadata in descending version order, bounded to 100 rows.
- List audit entries in descending event order, bounded to 100 rows.
- Require the trusted Mono administrator context for both queries.
- Do not return configuration JSON in the version listing.
- Keep the queries internal and out of public API routing.

## Acceptance criteria

1. A missing or non-admin operations identity fails before database access.
2. Results are scoped by explicit customer and service and use validated identifiers.
3. Limits outside 1–100 are rejected.
4. Version metadata omits configuration payload; audit results expose only the recorded audit fields.
5. PostgreSQL integration tests cover visibility, ordering, role restriction, and limits.

## Out of scope

- Staff authentication implementation, UI/API routes, payload edit/review, Hana seed data, Stage, and deployment.
