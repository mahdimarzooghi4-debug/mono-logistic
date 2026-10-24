# Delivery Request Route Location Binding v1

## Stable IDs

For a persisted request `drq_{32-hex}`, Mono derives these opaque route location IDs:

- Pickup: `loc_{32-hex}_pickup`
- Drop-off: `loc_{32-hex}_dropoff`

The IDs are deterministic for that request and do not include address, name, or phone data. The delivery-request create response includes both IDs and `route_location_coordinates_status`.

## Coordinate registration

- When both pickup and drop-off have complete coordinate pairs, the intake flow seeds both IDs into the versioned coordinate catalog using deterministic idempotency command IDs and expected version zero.
- `Registered` means the catalog accepted or replayed both writes.
- `Conflict` means an existing coordinate binding differed; the intake does not overwrite it.
- `CoordinatesPending` means at least one location has no supplied coordinate pair. The system does not geocode addresses as part of delivery intake.
- A transient failure between the two writes can leave one ID registered. Replaying the same delivery request retries both commands safely; routing still fails closed while either ID is missing.

This binding connects the authorized request submitter's validated coordinate pair to the route matrix acquisition module. It does not create a Mission or Route, select a provider, approve an address, or establish capacity, Zone, feasibility, or dispatch authorization.
