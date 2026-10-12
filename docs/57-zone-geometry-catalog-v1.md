# Zone geometry catalog v1

The operations-only geometry catalog provides a paginated view of immutable zone geometry snapshots. It is intended for operational inspection and does not change a snapshot, its review, activation, delivery eligibility, or route planning.

## Endpoint

`GET /operations/v1/zones/geometry-catalog`

The endpoint requires the Mono administrator authorization policy and the service independently verifies the authenticated operations context.

| Query parameter | Behavior |
| --- | --- |
| `kind` | Optional exact value: `City`, `OperatingZone`, or `MicroZone`. |
| `pageSize` | Optional integer from 1 to 100; defaults to 50. |
| `cursor` | Opaque keyset cursor returned by the prior page. |

The response contains the snapshot identity and kind, source ID and revision, attribution, SRID and geometry type, geometry and snapshot SHA-256 hashes, recorded time, whether this exact version is active, its latest review decision when present, and an integrity verification flag. It also returns `hasMore` and `nextCursor`.

Ordering is deterministic by zone ID and geometry version. The next page uses a keyset cursor. A malformed cursor, unknown kind, or invalid page size returns HTTP 400.

## Integrity and scope

Before a row is returned, the service reloads it through `PostgresZoneGeometryRepository.FindAsync`, which checks WKB and snapshot hashes, SRID, geometry type, and geometry validity. The service also checks the active pointer hash against the immutable snapshot hash. A detected mismatch fails the request rather than labeling the row verified.

The catalog is read-only. It does not import OpenStreetMap data, create reviews, activate geometry, expose polygon coordinates, or feed the coverage preview, order acceptance, customer eligibility, or dispatch.
