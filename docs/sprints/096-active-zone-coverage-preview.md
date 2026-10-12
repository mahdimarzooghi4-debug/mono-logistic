# Sprint 096 — Active zone coverage preview

## Delivered
- Add an operator-only coordinate preview against the exact active geometry snapshots.
- Report City, OperatingZone, and MicroZone separately with explicit match, outside, boundary, and ambiguity states.
- Distinguish a level with no active geometry from an outside result.
- Verify the active pointer hash matches the immutable version and revalidate geometry snapshot hashes on read.
- Keep the preview advisory and disconnected from order intake, eligibility, routing, and activation.

## Verification
- PostgreSQL integration tests cover active-version-only reads, hierarchical results, overlaps/shared boundaries, empty geometry configuration, and invalid coordinates.
