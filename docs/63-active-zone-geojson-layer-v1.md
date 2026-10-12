# Active zone GeoJSON layer v1

This operations endpoint provides a paginated GeoJSON FeatureCollection for the currently active geometry snapshots of one zone kind.

## Endpoint

`GET /operations/v1/zones/active-geometries/{kind}?pageSize=10&cursor=...`

Supported kinds are `City`, `OperatingZone`, and `MicroZone`. The endpoint requires Mono administrator authorization. Page size is 1–25, ordered by zone ID, and the next page uses an opaque keyset cursor.

Only rows referenced by `zone_geometry_active` are included. The active pointer's hash is checked against its immutable snapshot, and each feature is produced through the integrity-checking GeoJSON snapshot service. The response wraps standard GeoJSON `FeatureCollection` data with the selected kind, feature and coordinate counts, and pagination fields. The total page is capped at 250,000 coordinates; reduce page size if a selected page exceeds that limit. Each page has a strong ETag derived from its active snapshot hashes and pagination state. Responses use `Cache-Control: private, no-cache`; GET `If-None-Match` uses weak entity-tag comparison, including lists and the wildcard. A match returns HTTP 304 after authorization and active pointers are revalidated.

## Scope

The result is an operator map layer backed by stored active snapshots. It does not query OSM, provide tiles, mutate activation, or expose data to customer/public clients.
