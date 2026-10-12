# Zone coverage completeness check v1

The coverage completeness diagnostic compares one selected parent snapshot with an explicit set of child snapshots. It reports uncovered portions of the parent and children that extend beyond it.

## Endpoint

`POST /operations/v1/zones/geometry-coverage-check`

Example request:

```json
{
  "parent": { "zoneId": "city-x", "geometryVersion": "2026-10-12" },
  "children": [
    { "zoneId": "west", "geometryVersion": "2026-10-12" },
    { "zoneId": "east", "geometryVersion": "2026-10-12" }
  ]
}
```

The parent kind determines the required child kind: City requires OperatingZone children; OperatingZone requires MicroZone children. The endpoint accepts 1–50 unique children, validates all snapshots through the integrity-checking geometry repository, and limits total coordinates to 100,000.

The response reports whether the selected child union leaves any gap in the parent, whether all children are covered by the parent, and an overall `passed` value. Gaps are represented by each uncovered polygon's longitude/latitude bounding box. Children extending beyond the parent are identified by snapshot reference.

## Interpretation and scope

This check evaluates only the snapshots supplied by the operator. Gaps may be intentional where service is excluded; the report is advisory and does not create a review, block activation, or change coverage preview, order eligibility, or dispatch. It does not import map data.
