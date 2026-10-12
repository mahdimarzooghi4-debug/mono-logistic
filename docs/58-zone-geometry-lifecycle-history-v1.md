# Zone geometry lifecycle history v1

An operations-only endpoint returns an immutable geometry snapshot together with its human review and activation lifecycle events in one chronological timeline.

## Endpoint

`GET /operations/v1/zones/{zoneId}/geometry/{geometryVersion}/history`

The endpoint requires the Mono administrator authorization policy and independently checks the authenticated operations context.

| Query parameter | Behavior |
| --- | --- |
| `pageSize` | Optional integer from 1 to 100; defaults to 50. |
| `cursor` | Opaque keyset cursor returned by the preceding page. |

The response includes snapshot identity, kind, source and attribution, geometry type and snapshot hash; the page of events; `hasMore`; and `nextCursor`. Events contain timestamp, event type (`REVIEW` or `ACTIVATION`), ID, action/decision, actor, reason, and snapshot hash. Ordering is deterministic by timestamp, event type, and event ID.

Unknown zone versions return HTTP 404. Invalid page sizes or cursors return HTTP 400. A mismatch between an event hash and the immutable version fails the request. The version itself is loaded through the geometry repository, which verifies the stored WKB and snapshot integrity.

## Scope

This endpoint is read-only and does not alter review or activation state. It does not import OSM data or affect the coverage preview, eligibility, or dispatch.
