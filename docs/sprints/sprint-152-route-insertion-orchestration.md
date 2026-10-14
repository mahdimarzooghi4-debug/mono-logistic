# Sprint 152 — Route Insertion Orchestration

## Goal

Connect route-sequence evaluation to immutable route-plan writes so a new Mission can only be added after re-evaluating every committed delivery deadline.

## Delivered

- Added an application service that loads the requested Route version and builds planner input from that exact stored snapshot.
- Added a source-verifier boundary and fail-closed default; the planner's legacy `SourceVerified` flag is set by the verifier result rather than trusted from the caller.
- Require the caller to select one of the planner's feasible pickup/drop-off positions before persisting.
- Persist through the versioned store with expected-version concurrency and idempotent command handling, including re-evaluation of retries against their original version.
- Added tests for verified persistence, unverified evidence, invalid position selection, and future/stale version conflicts.

## Scope boundary

This is not an overall feasibility or dispatch decision. It does not configure a routing provider, add a route HTTP API, evaluate capacity or Zones, or change Mission state. Until a route evidence verifier is configured, insertion remains unresolved.
