# Route Travel-Time Evidence v2

## Acquisition boundary

`RouteMatrixAcquisitionService` resolves the start, existing stop, and candidate pickup/drop-off IDs through `IRouteLocationCoordinateResolver`, then requests a square directed matrix through `IMapsGateway`. It preserves the requested `RouteTravelMode` and maps each provider cell back to its origin/destination IDs. Missing or invalid coordinates fail closed. Unreachable provider pairs remain null duration cells.

The service returns unsigned evidence with `SourceVerified = false` and an empty attestation. A trusted source must attest to the payload, and the configured verifier must accept it before route planning can use the matrix. Acquisition itself grants no feasibility or dispatch authority.

## Signed payload

Evidence v2 computes lowercase SHA-256 over compact UTF-8 JSON serialized with `System.Text.Json` default settings. Property order is fixed:

1. `contract` — `mono-route-travel-time-evidence.v2`
2. `route_id`
3. `route_version`
4. `route_snapshot_sha256`
5. `candidate` — Mission ID, pickup/drop-off IDs, timestamps, and service durations
6. `evidence_id`
7. `captured_at`
8. `source_id`
9. `travel_mode` — `Driving`, `Cycling`, or `Walking`
10. `locations` — exact route location IDs sorted ordinally, with latitude and longitude encoded using invariant round-trip (`R`) formatting
11. `matrix_complete`
12. `cells` — all supplied origin rows and destination cells, sorted ordinally, with nullable duration seconds

The source signs the digest using the existing HMAC-SHA256 v1 attestation contract and scope `feasibility:RouteInsertion`. `SourceVerified` remains application-owned and is excluded from source evidence.

## Limits and integration

The location resolver, maps provider, trusted source, and signing key must be provided by deployment integration. This module selects no vendor, host, credential, or operating mode. Evidence v1 signatures do not verify under v2. No route matrix provider or coordinate catalog is enabled by this change.
