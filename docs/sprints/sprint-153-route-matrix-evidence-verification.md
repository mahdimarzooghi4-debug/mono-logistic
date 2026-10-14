# Sprint 153 — Route Matrix Evidence Verification

## Goal

Bind route-time evidence to the exact route snapshot and proposed Mission, then verify its source using Mono's existing configured HMAC evidence registry.

## Delivered

- Added a canonical SHA-256 source payload for the route ID/version, immutable snapshot hash, insertion candidate, evidence ID/time/source, and directed travel-time cells.
- Added shape checks for a complete matrix covering every required origin/destination pair; negative durations and missing cells are rejected before source verification.
- Added a configured verifier adapter using the existing `feasibility:RouteInsertion` trust scope, signature validation, key rotation, and freshness policy.
- Require `Mono:RoutePlanning:EvidenceCustomerId` to select the evidence trust namespace; no namespace or trusted source is enabled by default.
- Added tests for valid signatures, matrix tampering, incomplete matrices, unapproved scope, missing namespace, and stale evidence.

## Scope boundary

This verifies evidence supplied to route planning; it does not fetch a matrix from a Maps provider or choose a provider/operator. Route insertion still requires a separately configured trusted source and an explicitly selected feasible position. It does not evaluate capacity or Zones, expose an API, create Missions, or authorize dispatch.

## Verification

See the exact-head Mono CI run recorded in the pull request. The test suite runs against PostgreSQL in CI.
