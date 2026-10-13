# Sprint 121 — Operations courier offer queue

## Goal

Give Mono operations staff a bounded read view of courier offers belonging to a Mission.

## Delivered

- Added `GET /operations/v1/missions/{missionId}/courier-offers`.
- The endpoint requires the Mono administrator authorization policy.
- Results are limited to the 100 most recently created offers for the Mission and include only offer ID, mission ID, courier ID, approved zone ID, current status, and timestamps.
- Invalid Mission IDs return a client error.
- Added PostgreSQL integration coverage for the queue read.

## Boundary

This is a read-only view of the current offer projection. It does not expose event internals or customer details. Offer withdrawal, automated recovery on courier suspension, map/GPS/OSM behavior, Stage, and deployment remain outside this change.
