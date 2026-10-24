# Route Travel-Time Evidence v1 (superseded)

This version is retained as the original payload definition. New attestations must use [Route Travel-Time Evidence v2](62-route-travel-time-evidence-v2.md), which binds the requested travel mode and the resolved coordinates for every route location.

## Purpose

Authenticate the source of a directed route-time matrix before the route insertion orchestrator uses it. This contract is separate from the planner's `SourceVerified` boolean; the application sets that flag only after the configured verifier returns `Verified`.

## Signed payload

The source computes a lowercase SHA-256 digest over compact UTF-8 JSON serialized with `System.Text.Json` default settings. The top-level properties are emitted in this order:

1. `contract` — `mono-route-travel-time-evidence.v1`
2. `route_id`
3. `route_version`
4. `route_snapshot_sha256` — `RoutePlanSnapshotValidator.ComputeSha256` of the exact immutable snapshot
5. `candidate` — Mission ID, pickup/drop-off location IDs, pickup-ready and committed-delivery timestamps, and both service durations
6. `evidence_id`
7. `captured_at`
8. `source_id`
9. `matrix_complete`
10. `cells` — origin rows sorted by ordinal location ID; each row's destinations are also sorted ordinally and includes the nullable duration in seconds

Timestamps use UTC round-trip (`O`) formatting with invariant culture. `SourceVerified` is deliberately excluded because it is an application-owned result, not source evidence. The matrix digest includes every supplied cell, including additional rows/cells, so no data can be changed after signing.

The source then creates the existing provider-neutral HMAC-SHA256 v1 attestation over the computed digest using scope `feasibility:RouteInsertion`, the configured evidence customer namespace, the source ID, evidence ID, and capture time. Signature format, key rotation, minimum key length, constant-time comparison, and freshness fields are defined in [Feasibility Evidence Source Verification v1](49-feasibility-evidence-source-verification-v1.md).

## Runtime configuration

- `Mono:RoutePlanning:EvidenceCustomerId` selects the customer namespace used for route evidence.
- `Mono:EvidenceSources:Enabled` must be explicitly enabled.
- A matching trusted-source entry must bind that customer ID, source ID, active key ID, a Base64 key with at least 256 bits, the `feasibility:RouteInsertion` scope, and freshness/clock-skew limits.

Missing configuration, unapproved scope, stale/future evidence, malformed or incomplete matrices, changed payloads, and invalid signatures fail closed. Secrets are runtime configuration and must not be committed to source control. No source or provider is enabled by this module.

## Boundary

This module verifies a supplied matrix; it does not call a Maps provider, resolve route location IDs to coordinates, select a routing operator, or obtain source attestations. Those remain explicit integration work. A verified time-window insertion is not overall feasibility, courier authorization, or dispatch permission.
