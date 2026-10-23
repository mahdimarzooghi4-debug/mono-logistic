# Sprint 232 — Configurable Valhalla Maps Provider

## Goal

Implement Mono's first provider adapter for the approved self-hosted OSM routing architecture, using Tehran as the pilot area.

## Delivered

- Add a server-side Valhalla adapter for route and route-matrix estimates behind `IMapsProvider`.
- Map Mono travel modes Walk/Bike/Car to Valhalla costing models pedestrian/bicycle/auto.
- Convert route and matrix output to Mono's metres and whole-second contract, preserve unreachable pairs, and validate response shape.
- Keep geocoding and reverse geocoding unavailable; Valhalla is not configured as a geocoder.
- Keep the safe `UnconfiguredMapsProvider` default. Enable Valhalla only when explicitly configured with `MonoMaps__Provider=Valhalla` and a private `MonoMaps__Valhalla__BaseUrl`.
- Add focused adapter tests and document the Stage-only private service prerequisite.

## Verification

Mono CI runs the adapter unit tests with the repository build, migration, and container checks. The exact run and result are recorded in the PR after completion.

## Boundary

No Valhalla server, Tehran OSM extract, map tile source, navigation app, credential, Render resource, Stage deployment, or Production service is provisioned. Tehran geographic quality and capacity still require a real dataset, representative courier-validated routes, and an owner-provided private host before Stage.
