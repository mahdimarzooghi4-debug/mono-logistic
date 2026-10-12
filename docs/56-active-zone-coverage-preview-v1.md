# Active zone coverage preview (Sprint 096)

Mono administrators can preview a WGS84 point against active snapshots:

- POST /operations/v1/zones/coverage-preview
- JSON body: { "latitude": 35.7, "longitude": 51.4 }

The response separates City, OperatingZone, and MicroZone levels. Each configured level reports Matched, Outside, Boundary, or Ambiguous and candidate IDs. A level without active boundaries reports HasActiveBoundaries=false and Status=null, so absence of data is never mistaken for confirmed lack of coverage.

The preview reads only the currently active version for each zone and validates its stored geometry hashes. It is advisory operations tooling. It does not create orders, determine customer eligibility, choose a zone on a shared edge, plan routes, or activate a version.
