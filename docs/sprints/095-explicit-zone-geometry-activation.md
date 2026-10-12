# Sprint 095 — Explicit zone geometry activation

## Delivered
- Add a single active-version pointer per zone with append-only activation/deactivation events.
- Require the exact immutable snapshot hash and the latest APPROVE review before activation.
- Serialize activation changes per zone and atomically record a version switch.
- Expose administrator operations for activate, deactivate, current-state inspection, and history.
- Keep activation metadata separate from eligibility, route planning, public APIs, and live OSM ingestion.

## Verification
- PostgreSQL integration tests cover approval gates, hash binding, switching, concurrent activation, deactivation conflicts, and immutable event history.
