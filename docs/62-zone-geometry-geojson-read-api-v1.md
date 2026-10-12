# Zone geometry GeoJSON read API v1

An administrator-only endpoint returns one immutable zone geometry snapshot as a GeoJSON Feature for an internal map client.

## Endpoint

`GET /operations/v1/zones/{zoneId}/geometry/{geometryVersion}/geojson`

The feature's geometry is a GeoJSON `Polygon` or `MultiPolygon` in WGS84 coordinates, with coordinate order `[longitude, latitude]`. Polygon exterior and interior rings are preserved. Properties include zone identity and kind, geometry version, source and attribution, geometry and snapshot SHA-256 hashes, and SRID.

The snapshot is loaded through the integrity-checking repository before serialization. Unknown versions return HTTP 404; invalid references return HTTP 400. Responses above 100,000 coordinates are rejected to keep a single request bounded. The snapshot hash is returned as a strong ETag. Responses use `Cache-Control: private, no-cache`; GET `If-None-Match` uses weak entity-tag comparison, including lists and the wildcard. A match returns HTTP 304 only after the administrator authorization policy has run, so the browser can reuse immutable payloads while each request revalidates access.

## Scope

This endpoint exposes the selected internal snapshot to an authenticated Mono administrator. It does not contact OSM or an external map provider, alter stored geometry, or activate the snapshot. Public/customer access and map tile delivery are not included.
