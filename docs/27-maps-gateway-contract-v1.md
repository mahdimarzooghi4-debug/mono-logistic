# Maps Gateway Contract v1

## Purpose

Mono uses one provider-neutral maps boundary. The logistics core depends on the gateway contract rather than a provider SDK or provider-specific request/response fields.

## Coordinates

- Coordinates use WGS84 latitude and longitude in decimal degrees.
- Latitude must be finite and within -90 through 90.
- Longitude must be finite and within -180 through 180.

## Operations

| Operation | Input | Result |
|---|---|---|
| Geocode | Address text | Coordinate and optional formatted address; `null` means no match |
| Reverse geocode | Coordinate | Address text; `null` means no match |
| Route | Origin, destination, optional waypoints | Route estimate; `null` means no route |
| Route matrix | Origin and destination lists | Rectangular rows by origin and columns by destination |

- Route distance is measured in metres.
- Estimated travel duration is measured in whole seconds.
- A matrix cell uses both distance and duration, or leaves both `null` when no route exists.
- Provider responses with invalid coordinates, negative/non-finite estimates, or incorrect matrix dimensions fail with a provider-contract error.
- Inputs and outputs preserve cancellation tokens across the provider boundary.

## Provider boundary

The repository defines `IMapsProvider` and `IMapsGateway` contracts plus input/output validation. It does not choose or configure a map provider, make network requests, or provide geocoding/routing results by itself. Provider implementation and runtime wiring remain separate work.
