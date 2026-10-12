# Zone geometry quality check v1

The advisory quality check detects pairs of selected snapshots whose polygon interiors overlap. It is designed to help operators inspect a proposed same-level zone set before human approval.

## Endpoint

`POST /operations/v1/zones/geometry-quality-check`

Requires Mono administrator authorization. Example request:

```json
{
  "kind": "OperatingZone",
  "snapshots": [
    { "zoneId": "north", "geometryVersion": "2026-10-12-a" },
    { "zoneId": "south", "geometryVersion": "2026-10-12-a" }
  ]
}
```

The request accepts 2–50 unique zone IDs, all of the requested kind, and at most 100,000 total geometry coordinates. Every snapshot is loaded through the integrity-checking geometry repository. A missing snapshot returns HTTP 404; invalid input returns HTTP 400.

The response reports snapshot and coordinate counts, a `passed` flag, and any `InteriorOverlap` findings with both zone/version references. Adjacent polygons that share only an edge or point are not reported as interior overlap. The check evaluates only the caller-selected set and does not infer gaps or completeness beyond those snapshots.

## Scope

This endpoint is a diagnostic only. It does not create a review, block approval or activation, modify geometry, or affect coverage preview, eligibility, or dispatch. It imports no external map data.
