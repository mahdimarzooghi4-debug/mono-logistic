# Sprint 233 — Courier Offer Map Context

## Goal

Expose only the active offer's pickup and drop-off coordinates to its assigned courier so the courier client can render the offer on an OSM-derived map.

## Delivered

- Add `GET /courier/v1/mission-offers/{offerId}/route-context` behind the existing courier authentication policy.
- Hide missing, expired, non-offered, wrong-courier, and non-searching Missions behind the same not-found response.
- Resolve current coordinates through the versioned route-location catalog using deterministic request-bound location IDs.
- Return coordinates as optional values and identify incomplete location data without substituting sample points.
- Exclude addresses and contact data; mark the response `Cache-Control: no-store`.
- Document that this is a marker-only context. Route geometry, ETA, tile hosting, and the Courier client are separate work.

## Verification

- Add tests for complete and partial coordinate contexts, safe response fields, and malformed inputs.
- Run repository CI against the exact PR head, including build, API tests, migrations, and container checks.
- Local .NET verification is unavailable in the current workspace because the .NET SDK is not installed.

## Boundary

No map tiles are fetched by the API. No courier location is included in this response. No address, customer identity, phone, or other contact data is exposed. No Valhalla server, map host, Stage, or Production service is enabled by this change.
