# Zone geometry review history (Sprint 094)

Mono administrators can record an APPROVE or REJECT review for a persisted geometry snapshot at:

- POST /operations/v1/zones/{zoneId}/geometry/{geometryVersion}/reviews
- GET /operations/v1/zones/{zoneId}/geometry/{geometryVersion}/reviews

A submitted decision must include the exact stored snapshot SHA-256, decision, and a reason. Each decision records the authenticated Mono actor and server timestamp. Review rows are append-only, and reviewers can see the full ordered history.

This is review evidence only. It does not activate a geometry, select a winning overlap, change delivery eligibility, import OSM data, or expose a public route. Any future activation workflow must define its own business rules and consume the reviewed snapshot explicitly.
