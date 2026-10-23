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

## Courier offer map context

- `GET /courier/v1/mission-offers/{offerId}/route-context` is available only to an authenticated courier.
- The endpoint returns coordinates only when the offer belongs to that courier, remains unexpired and `OFFERED`, and its Mission remains `SEARCHING_COURIER`.
- Pickup and drop-off coordinates are read from the current versioned route-location coordinate catalog. Missing coordinates remain `null` and set `coordinates_status` to `COORDINATES_PENDING`; the client must not invent a marker.
- The response deliberately omits addresses, names, phone numbers, and contact details, and uses `Cache-Control: no-store`.
- This read contract supplies map markers only. It does not return an estimated route line or live ETA. Those require configured routing evidence and are separate from OSM tile rendering.
- Map tiles are loaded by the client from a configured OSM-derived tile source, not through the Maps Gateway. The client must display required attribution and support replacing the tile source without changing the courier API contract.
- `POST /courier/v1/location` accepts the authenticated courier's GPS point; courier identity comes from authentication, never the request body. Only the newest `observed_at` point is retained per courier, and older or duplicate updates return `applied: false` without moving the projection backwards.
- Location writes return only courier ID, observation time, and whether the point was applied. Coordinate data is not echoed to callers. The current-point table is an operational projection for routing and is subject to location-data retention and privacy policy before production use.
