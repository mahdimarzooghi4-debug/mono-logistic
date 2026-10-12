# OpenStreetMap Location Source Decision v1

## Status

Accepted product/technical direction; implementation deferred.

## Decision

Mono will use OpenStreetMap (OSM) as the geographic map-data source for future map and location capabilities.

GPS coordinates are obtained from the user's device geolocation capability, subject to user permission and a future Mono location/privacy policy. OpenStreetMap is the map dataset used to render and interpret geographic context; it is not the GPS sensor or a commitment to a particular tile, geocoding, or routing service.

The existing provider-neutral Maps Gateway remains the integration boundary. The specific tile host, geocoder, router/ETA operator, hosting model (managed OSM-derived provider or self-hosted), and rollout configuration remain open for a later technical decision. No external service, credentials, or production endpoint is enabled by this decision.

## Implementation constraints

- Preserve the provider-neutral Maps Gateway so service operators can be changed through configuration without changing Mono's core.
- Show visible OpenStreetMap attribution on map views (for example, “© OpenStreetMap contributors”) and review applicable ODbL obligations for any published or derived datasets.
- Do not assume community-operated public endpoints are production infrastructure. The OSM Foundation's standard tile service is best-effort, has no SLA, and has capacity and usage restrictions. Its public Nominatim service has a strict usage policy and is not suitable as a default high-volume or autocomplete service. Re-check the policies and expected traffic before selecting any endpoint.
- Do not send personal or confidential information to OSM Foundation services. Keep precise courier GPS ingestion separate from map rendering/geocoding, and define consent, authorization, retention, and access controls before collecting or storing live location.
- Select a compliant OSM-derived hosted provider or self-hosted service after requirements for map tiles, geocoding, routing, coverage, traffic, availability, privacy, and operating cost are known.

## Source references

- OSM Tile Usage Policy: https://operations.osmfoundation.org/policies/tiles/
- Nominatim Usage Policy: https://operations.osmfoundation.org/policies/nominatim/
- OpenStreetMap data licence and use cases: https://wiki.openstreetmap.org/wiki/License/Use_Cases

These policies can change and must be rechecked when implementation is planned.
