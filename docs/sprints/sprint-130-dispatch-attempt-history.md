# Sprint 130 — Mission dispatch attempt history

## Goal

Give operations staff a single, auditable view of every courier offer attempt for a Mission, including why and when the latest outcome occurred.

## Delivered

- Added `GET /operations/v1/missions/{missionId}/dispatch-attempts`, restricted to Mono administrators.
- Returns up to 100 attempts newest first with courier, zone, current projected offer status, creation/update times, expiry deadline, and the latest immutable event's action, actor type, reason, and timestamp.
- Returns an empty attempts list for a valid Mission that has not been offered to a courier, and a not-found response for an unknown Mission.
- Added the `MISSION_DISPATCH_ATTEMPT_HISTORY` read-audit action and migration 050.
- Added integration coverage for withdrawn, suspension-withdrawn, released, and expired attempts, including the audit record.

The existing per-offer events endpoint remains available for full event-by-event inspection. This endpoint provides a compact mission-level comparison of attempts and outcomes.
