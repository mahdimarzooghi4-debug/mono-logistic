# Zone Model v1

## Purpose

Represent Mono's geographic hierarchy as City → Operating Zone → Micro-zone while keeping geometry and capacity behavior outside the model.

## Hierarchy

- A City is a root and has no parent.
- An Operating Zone must reference a City parent.
- A Micro-zone must reference an Operating Zone parent.
- Zone IDs are opaque, required, and unique within the validated registry.
- Names are required.

The City node is the canonical city context for its descendant operating and micro-zones.

## Metadata boundaries

- geometry_reference remains an optional opaque link from a zone definition to a separately versioned geometry snapshot.
- Sprint 092 adds validated WGS84 Polygon/MultiPolygon snapshots, with GeoJSON coordinate order (longitude, latitude), source/revision/attribution metadata, and point classification as inside, outside, or boundary.
- A registry lookup only resolves within one requested hierarchy level. Overlap or shared-edge ambiguity is returned explicitly and never silently chooses a zone.
- status is optional opaque metadata; this contract defines no status values or transitions.
- capacity_policy_id is optional opaque metadata; this contract does not calculate or allocate capacity.
- An empty registry is valid and means no zone definitions have been supplied.

## Out of scope

The geometry primitive does not activate or retire zone snapshots, authorize couriers, decide delivery coverage, calculate capacity, persist definitions, or expose operations APIs. No OSM boundary data is imported by Sprint 092. A Zone is a hard boundary in the approved business architecture, but operational enforcement still depends on trusted data ingestion, persistence/lifecycle, and authorization.
