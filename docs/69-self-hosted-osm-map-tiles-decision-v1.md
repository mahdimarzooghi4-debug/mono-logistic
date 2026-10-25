# Self-Hosted OpenStreetMap Map Tiles Decision v1

## Status

Accepted direction for implementation planning. No tile service is deployed by this decision.

## Context

Mono needs a base map so couriers and operators can see pickup and delivery locations. Device GPS supplies coordinates, not map imagery. Mono has selected OpenStreetMap (OSM) as its geographic data source and is pursuing a self-hosted Valhalla candidate for server-side route and distance estimates. Map display and routing are separate capabilities.

The project has an Iran OSM PBF extract available for a later build and validation. The actual generated archive size, useful zoom coverage, server requirements, and traffic capacity have not yet been measured.

## Decision

Mono will build and serve its own OSM-derived **vector map tiles** for the initial Iran deployment. The intended pipeline is:

1. Keep a dated, checksummed copy of the source PBF.
2. Use Planetiler to build a versioned MBTiles archive from that PBF.
3. Serve the archive through an owner-operated tile service such as TileServer GL, behind Mono's HTTPS map hostname.
4. Render the configured style and tiles in the client with MapLibre.

The tile endpoint and style URL must be configuration-driven so Mono can replace the tile-serving implementation without changing logistics business logic. Do not use the public `tile.openstreetmap.org` service as Mono's production tile dependency.

## Boundaries

- This decision covers base-map rendering only. It does not implement geocoding, reverse geocoding, routing, ETA, live traffic, or turn-by-turn navigation.
- The courier flow remains: show pickup/drop-off markers and available estimates in Mono; when the courier requests navigation, open an installed navigation app for the current leg.
- Valhalla remains a separate routing service behind the existing Maps Gateway. Its route data and the rendered map tiles may come from the same dated OSM source snapshot, but their build, runtime, health checks, and capacity are independent.
- No third-party map API key or credentials are needed for the self-hosted tile path. Public OSM data downloads and other upstream sources must still follow their respective terms.

## Data build and refresh

Planetiler produces full-snapshot outputs rather than applying incremental OSM diffs. For each refresh:

- retain the source extract date, URL/source, checksum, build version, and build logs;
- build a new archive without overwriting the currently served one;
- validate the new archive, then switch the served version atomically;
- retain the previous known-good archive until rollback is no longer needed.

Refresh cadence is not set here. It must be chosen based on the pilot's acceptable map-data age, operational capacity, and source availability. A failed or incomplete build must not replace the active archive.

## Compliance and privacy

- Display visible attribution, for example “© OpenStreetMap contributors”, on every map view.
- Review the ODbL obligations for the specific data use and distribution before public release.
- Do not send courier/customer coordinates or other personal information to OpenStreetMap Foundation services.
- Keep GPS collection, consent, access control, and retention governed by Mono's location/privacy policy independently from tile requests.

## Before Stage

The implementation is not Stage-ready until the team has:

1. Processed the available Iran PBF and recorded its checksum, build tool/version, generated archive size, and build resource use.
2. Opened the generated map in the actual Mono client and visually checked Tehran at the required zoom levels, including roads, labels, and pickup/drop-off markers.
3. Verified attribution and ODbL review, and tested archive integrity and missing-tile behavior.
4. Benchmarked tile requests using representative client views and expected concurrency; sized CPU, memory, disk, and bandwidth from those measurements.
5. Configured HTTPS, access/network policy, health monitoring, backup/rollback, and a documented map-data refresh procedure.
6. Made the client use the configured owner-operated tile/style URL and verified it does not silently fall back to a public tile provider.

Until these checks and actual server provisioning are complete, the self-hosted tile service is a selected direction, not an available production capability.
