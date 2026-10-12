# Sprint 092 — Versioned Zone Geometry & Point Resolution

## Goal

Build the geometry foundation needed to enforce Mono's hard operating-zone boundaries using the previously selected OSM map-data direction, without importing or trusting real OSM data yet.

## Delivered

- Add NetTopologySuite 2.6.0 as the geometry engine.
- Add immutable versioned WGS84 zone boundary snapshots for Polygon and MultiPolygon geometry.
- Validate geometry type, SRID 4326, coordinate ranges, topology, source revision, and attribution metadata.
- Classify a WGS84 point as inside, outside, or on the boundary. Polygon holes remain outside.
- Resolve points within one zone hierarchy level and report boundary/overlap ambiguity without silently selecting a zone.
- Extend the product backlog and zone model documentation.

## Security and rollout boundary

No OSM import, external map call, API endpoint, database migration, live courier location collection, zone activation, or operational delivery eligibility decision is added. Geometry snapshots must be explicitly supplied to the pure domain resolver. The OSM dataset revision and attribution are required metadata, but this sprint does not verify source authenticity.

## Acceptance criteria

- WGS84 geometry uses GeoJSON's longitude/latitude coordinate order.
- Invalid, empty, non-polygonal, wrong-SRID, out-of-range, antimeridian-crossing, or topologically invalid geometry is rejected.
- A point on one boundary is not accepted as an inside match.
- Overlapping interiors and shared boundaries are returned as ambiguous.
- City, Operating Zone, and Micro-zone matching is performed only for the requested hierarchy level.

## Validation

- Added focused tests for Polygon/MultiPolygon containment, holes, boundaries, overlaps, hierarchy separation, invalid geometry, and invalid coordinates.
- NetTopologySuite official releases: https://github.com/NetTopologySuite/NetTopologySuite/releases
