# Persistent Zone Geometry Snapshots v1

## Purpose

Store validated zone boundaries durably without making any version active or using it to decide delivery eligibility.

## Storage contract

- A snapshot is keyed by the global `zone_id` and opaque `geometry_version`; zone definitions are currently platform-wide, so no customer tenant key is introduced.
- Store zone kind, source ID, source dataset revision, attribution text, WGS84 SRID (4326), geometry type, WKB bytes, geometry SHA-256, full snapshot SHA-256, and server-recorded UTC time.
- Only validated Polygon and MultiPolygon boundaries are accepted by the repository.
- Identical writes to an existing key return `Replayed`. Different content under the same key returns `Conflict`. New version identifiers append a new snapshot.
- Rows cannot be updated or deleted. Read paths verify the geometry hash and full snapshot hash, then revalidate geometry/SRID/topology before returning it.

## Activation and authority

This is storage only. It has no active-version pointer, effective interval, approval workflow, OSM data importer, endpoint, or runtime service-coverage decision. Applications must continue treating zone geometry as unavailable for eligibility until separate lifecycle and authority rules are implemented.

No OSM boundary data is seeded. Future imports must preserve dataset revision and attribution metadata and separately implement license-aware distribution, source verification, and operational review.
