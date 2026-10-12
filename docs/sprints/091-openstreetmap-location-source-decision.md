# Sprint 091 — OpenStreetMap Location Source Decision

## Goal

Record the product decision to use OpenStreetMap as Mono's map-data source while preserving the existing provider-neutral Maps Gateway.

## Scope

- Distinguish device GPS/geolocation from geographic map data.
- Record OSM attribution and ODbL review requirements.
- Keep tile hosting, geocoding, routing/ETA operators, hosting model, privacy controls, and production endpoints undecided until implementation planning.
- Avoid treating OSM Foundation's public tile or Nominatim services as an assumed production dependency.

## Changes

- Updated `docs/18-product-backlog-v1.md` with Sprint 091 under Maps Gateway.
- Added `docs/50-openstreetmap-location-source-decision-v1.md` with scope, constraints, official policy references, and deferred implementation choices.

## Acceptance criteria

- OSM is recorded as the selected map-data source.
- Device geolocation is accurately identified as the GPS fix source, separate from OSM.
- No provider endpoint, credential, production integration, GPS collection, storage, or deployment is enabled.
- Existing provider-neutral maps abstractions remain the integration path.

## Validation

- Documentation reviewed against the current official OSM Foundation tile and Nominatim usage policies and OSM's ODbL use-case guidance.
