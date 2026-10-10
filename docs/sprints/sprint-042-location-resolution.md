# Sprint 042 — Location Resolution

- Status: In progress
- Phase: B — Logistics Core
- Epic: E5 — Maps, Geocoding & Location
- Capability: E5.2 — Location Validation
- Branch: sprint-042-location-resolution
- Base: sprint-041-maps-gateway-contract

## Sprint goal

Add a provider-neutral location resolver that validates supplied WGS84 coordinates and uses the Maps Gateway to resolve address-only locations.

## Scope

- Treat a valid supplied coordinate pair as authoritative without calling geocoding.
- Reject partial, out-of-range, or absent location data.
- Resolve address-only locations through the existing IMapsGateway.
- Preserve an unresolved result when geocoding has no match.
- Preserve cancellation through the gateway call.
- Document that coordinates do not imply coverage or zone eligibility.

## Acceptance criteria

1. Partial coordinate pairs are rejected before any gateway call.
2. Valid coordinates are returned as ProvidedCoordinates and do not trigger geocoding.
3. Address-only input is trimmed and sent to the provider-neutral gateway.
4. A gateway no-match returns no resolved location.
5. Provider-formatted address is preferred, with normalized input address as fallback.
6. Zone resolution remains deferred until E6.1 defines the zone registry and geometry.
7. Delivery-request intake, external provider configuration, coverage decisions, Stage, and deployment are unchanged.

## Out of scope

Maps-provider selection or runtime wiring, delivery-request intake integration, zone geometry and membership, coverage/feasibility decisions, route selection, Stage, and deployment.
