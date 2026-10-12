# Sprint 094 — Zone geometry review audit

## Delivered
- Added immutable review records tied by foreign key to exact geometry versions.
- Added MFA-backed Mono administrator routes to record and inspect APPROVE/REJECT decisions.
- Bound every review to the expected snapshot SHA-256 and stored actor, reason, and time.
- Kept this workflow separate from geometry activation, eligibility, routing, and live OSM ingestion.

## Verification
- PostgreSQL integration tests cover decision history, hash mismatch, missing snapshot, and append-only protection.
- CI is the compilation and test gate for this repository.
