# OpenStreetMap Location Source Decision v1

## Status

Accepted product/technical direction. OSM is the geographic data source. Mono will self-host OSM-derived vector tiles for map display under [Self-Hosted OpenStreetMap Map Tiles Decision v1](69-self-hosted-osm-map-tiles-decision-v1.md). The server-side routing decision is specified by [Self-Hosted OSM Routing Decision v1](68-self-hosted-osm-routing-decision-v1.md).

## Decision

Mono will use OpenStreetMap (OSM) as the geographic map-data source for future map and location capabilities.

GPS coordinates are obtained from the user's device geolocation capability, subject to user permission and a future Mono location/privacy policy. OpenStreetMap is the map dataset used to render and interpret geographic context; it is not the GPS sensor.

Mono will run Valhalla as the first self-hosted route-engine candidate behind the provider-neutral Maps Gateway for server-side route and distance-matrix estimates. For base-map rendering, Mono will build and serve its own OSM-derived vector tiles from the Iran PBF extract. The tile service and route engine are separate services with separate deployment, health, capacity, and refresh procedures.

Map tiles will be generated as a versioned MBTiles archive with Planetiler and served from an owner-operated HTTPS map endpoint. MapLibre clients will render the tiles with a configurable style and endpoint. The available PBF has not yet been processed or benchmarked; no server sizing or Stage readiness is claimed.

Geocoding, reverse geocoding, live traffic, and an external mobile navigation app are not selected by this decision.

## Implementation constraints

- Preserve the provider-neutral Maps Gateway so routing implementations can be changed through configuration without changing Mono's core.
- Keep client tile/style URLs configurable and do not make the public `tile.openstreetmap.org` service a production dependency.
- Show visible OpenStreetMap attribution on map views (for example, “© OpenStreetMap contributors”) and review applicable ODbL obligations for the specific published or derived datasets.
- Do not send personal or confidential information to OSM Foundation services. Keep precise courier GPS ingestion separate from tile delivery, and define consent, authorization, retention, and access controls before collecting or storing live location.
- Build a new dated tile archive separately, validate it, then activate it atomically; retain the previous known-good archive for rollback.
- Before Stage, process and validate the actual Iran extract, inspect Tehran at required zoom levels, measure archive and runtime resource use, benchmark expected traffic, configure monitoring/HTTPS/rollback, and document the data refresh process.
- Public OSM tile and geocoding endpoints are not Mono production infrastructure. If any upstream endpoint is considered for a separate capability, verify its current terms and capacity first.

## Source references

- OSM Tile Usage Policy: https://operations.osmfoundation.org/policies/tiles/
- Nominatim Usage Policy: https://operations.osmfoundation.org/policies/nominatim/
- OpenStreetMap data licence and use cases: https://wiki.openstreetmap.org/wiki/License/Use_Cases

These policies can change and must be rechecked when implementation is planned.
