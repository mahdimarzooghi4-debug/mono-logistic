# Sprint 041 — Maps Gateway Contract

- Status: In progress
- Phase: B — Logistics Core
- Epic: E5 — Maps, Geocoding & Location
- Branch: `sprint-041-maps-gateway-contract`
- Base: `sprint-040-eligible-modes-preview`

## Sprint goal

Create the provider-neutral Maps Gateway boundary and validate maps inputs and provider outputs without selecting a provider.

## Scope

- Define contracts for geocoding, reverse geocoding, route estimates, and route matrices.
- Use WGS84 coordinate validation.
- Standardize route distance as metres and estimated duration as whole seconds.
- Validate route matrix dimensions and nullable unreachable cells.
- Preserve cancellation tokens through provider calls.

## Acceptance criteria

1. Invalid/non-finite coordinates are rejected before a provider call.
2. Invalid provider coordinates and route estimates fail with a provider-contract error.
3. Route matrix dimensions must match the origin and destination counts.
4. A matrix cell either has both estimate values or neither.
5. No provider SDK, network call, or runtime provider configuration is introduced.

## Out of scope

Provider selection, geocoding/routing implementation, external network access, delivery-request integration, coverage or promise decisions, Stage, and deployment.
