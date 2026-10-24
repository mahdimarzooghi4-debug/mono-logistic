# Self-Hosted OSM Routing Decision v1

## Status

Accepted architectural direction for Phase 1. Valhalla is selected as the first self-hosted routing-engine candidate. Production activation is not approved by this document.

## Context

Courier offers need to show the merchant pickup and customer drop-off coordinates on a map. The courier should be able to see an approximate route distance and travel time before accepting an offer. After acceptance, the courier app can open a navigation app installed on the device, first with the merchant as destination and then, after pickup confirmation, with the customer as destination.

Mono already defines a provider-neutral Maps Gateway and trusted route evidence contracts. Route distance and duration are consumed by pricing, feasibility, route planning, and promise evaluation; they cannot be replaced by a visual straight line or by launching an external navigation app.

## Decision

- Continue using OpenStreetMap as the base geographic dataset.
- Run an open-source route engine inside Mono-controlled infrastructure for server-side route and distance-matrix calculations. Use Valhalla as the first implementation candidate because its upstream project provides route, time/distance matrix, map-matching, and route-optimization capabilities over OSM data.
- Keep all use behind `IMapsProvider` / `IMapsGateway`. Provider-specific fields must not enter domain or pricing contracts.
- Support the Phase 1 modes Car, Bike, Walk, and Motorcycle only after route-profile behavior is validated for the selected pilot area. Bike maps to a bicycle profile; Motorcycle maps to Valhalla's `motor_scooter` costing as the initial candidate, subject to Tehran route-quality validation. A mode that is unsupported or cannot produce valid evidence remains unavailable; do not silently substitute another mode.
- The courier-facing map is for viewing pickup/drop-off and route estimates. The “Navigate” action may launch an installed external navigation app by coordinates. The external app is not an authority for Mono pricing, feasibility, committed ETA, or route evidence.
- Treat ETA from the OSM route engine as a route-model estimate without live traffic unless a separately approved, trusted traffic source is added. Label it as approximate and do not claim real-time traffic accuracy.
- Keep map rendering and tile hosting as a separate decision. MapLibre may be evaluated as a renderer; it does not itself supply OSM tiles or route calculations.
- Do not use OpenStreetMap Foundation public tile or Nominatim endpoints as production infrastructure. Select a permitted tile source or operate our own tile service before production map display. Show required OSM attribution and review ODbL obligations for distributed or derived datasets.
- Store and refresh the selected OSM extract through a documented, reproducible pipeline. Do not upload courier/customer PII or precise live GPS to public OSM services.

## Why Valhalla first

The Phase 1 gateway already needs route estimates and matrices for multiple delivery modes, with map matching useful for noisy courier GPS. Valhalla's upstream project lists these capabilities in one OSM-based open-source engine. OSRM also supports configurable car, bike, and foot profiles, but the initial Mono evaluation benefits from testing Valhalla's broader matrix and map-matching feature set against the existing gateway contract. This is a candidate selection for implementation, not a claim that it has already passed coverage, latency, or quality tests.

## Initial pilot area

The initial routing pilot is Tehran. The exact extract boundary and representative delivery corridors must be set before loading data; tests must use real Tehran roads and courier-validated sample journeys.

## Required verification before Stage

1. Build the Tehran extract and document its source, boundary, timestamp, and reproducible refresh procedure.
2. Build Valhalla from a pinned, reviewed image/version with a reproducible OSM data import.
3. Contract-test route and matrix results for Car, Bike, Walk, and Motorcycle, including unreachable pairs, cancellation, invalid coordinates, and empty results.
4. Compare representative routes with local courier knowledge and record route-distance/ETA error; never treat synthetic tests as geographic quality evidence.
5. Load-test expected dispatch and matrix volume; set resource limits, readiness checks, update/rollback procedure, and monitoring.
6. Verify the route evidence source attestation is bound to the internal Valhalla service and preserves existing verification gates.
7. Decide and test the mobile map renderer/tile source and external-navigation deep-link fallbacks separately.

## Consequences

- No per-request commercial routing API is required for the selected self-hosted service, but compute, storage, map-data refresh, observability, and operational ownership remain Mono costs.
- Route estimates can be centrally consistent across phones. The routing service must remain private to Mono and must not be exposed as an unauthenticated public endpoint.
- Live traffic, geocoding/address search, map tiles, and third-party navigation applications remain separate capabilities and are not implied by this decision.
- Stage integration must remain disabled until the city, resource sizing, data refresh, and owner-supplied infrastructure prerequisites are resolved.

## References

- Valhalla upstream: https://github.com/valhalla/valhalla
- OSRM profile documentation: https://project-osrm.org/docs/v26.6.1/profiles
- OSM tile usage policy: https://operations.osmfoundation.org/policies/tiles/
- OSM attribution guidelines: https://osmfoundation.org/wiki/Licence/Attribution_Guidelines
- Existing Mono contracts: [Maps Gateway](27-maps-gateway-contract-v1.md), [Route Travel-Time Evidence v2](62-route-travel-time-evidence-v2.md), and [OpenStreetMap Location Source Decision v1](50-openstreetmap-location-source-decision-v1.md)
