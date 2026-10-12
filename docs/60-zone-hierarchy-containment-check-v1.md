# Zone hierarchy containment check v1

The operations-only containment diagnostic checks that selected child-zone snapshots are fully covered by one selected parent snapshot. It supports `City → OperatingZone` and `OperatingZone → MicroZone`.

## Endpoint

`POST /operations/v1/zones/geometry-containment-check`

Example:

```json
{
  "parentKind": "City",
  "parent": { "zoneId": "city-x", "geometryVersion": "2026-10-12" },
  "childKind": "OperatingZone",
  "children": [
    { "zoneId": "north", "geometryVersion": "2026-10-12" },
    { "zoneId": "south", "geometryVersion": "2026-10-12" }
  ]
}
```

The request accepts 1–50 unique child zones. Parent and children are read from immutable storage and validated with the existing geometry repository integrity checks. The total coordinate count is capped at 100,000. A missing snapshot returns HTTP 404 and invalid inputs return HTTP 400.

The response contains the selected parent and child levels, counts, a `passed` flag, and `OutsideParent` findings. Child polygons that touch or follow the parent's border are considered covered when the full polygon lies on or inside the parent. A child polygon that extends even partly outside is reported.

## Scope

This is advisory and does not activate geometry, create a review, block an approval, or change coverage, eligibility, dispatch, or map imports. The operator chooses the exact snapshots being compared.
