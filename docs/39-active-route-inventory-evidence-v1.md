# Active Route Inventory Evidence v1

## Purpose

Provide the E7.1 active-routes criterion with a point-in-time, per-Zone inventory result that can later be consumed by route-insertion evaluation.

## Contract

An inventory identifies a Zone and observation time, declares its source completeness, and records each known route's activity as ACTIVE, INACTIVE, or UNRESOLVED.

- A complete inventory with only known route states returns `PASS`; a complete empty inventory is valid.
- An incomplete or unavailable source returns `UNRESOLVED`.
- Any route with unknown activity returns `UNRESOLVED`, even if the inventory source otherwise declares itself complete.
- Route IDs must be unique and reason codes canonical.

## Boundary

An empty active-route inventory does not mean delivery is impossible; route insertion or new-route simulation may still be evaluated later. This evaluator consumes a caller-supplied completeness assertion and does not verify source trust, define route lifecycle, enforce freshness, persist route data, simulate insertion, create a Mission, or commit a Promise.
