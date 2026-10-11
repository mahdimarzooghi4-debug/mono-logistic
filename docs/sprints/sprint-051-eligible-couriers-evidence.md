# Sprint 051 — Eligible Courier Evidence Criterion

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1 — Eligible couriers criterion
- Branch: sprint-051-eligible-courier-evidence
- Base: sprint-050-eligible-modes-criterion

## Sprint goal

Add a fail-closed evidence evaluator for the eligible-couriers criterion that distinguishes definitive exhaustion from incomplete candidate discovery.

## Scope

- Accept an explicit candidate-set completeness state: COMPLETE, INCOMPLETE, or UNAVAILABLE.
- Validate unique opaque courier candidate identifiers and canonical reason codes.
- Pass when at least one candidate is explicitly eligible.
- Fail only when the set is complete and every candidate is definitively excluded.
- Preserve UNRESOLVED for incomplete/unavailable sets or unresolved candidates.
- Verify the criterion alone leaves the eight-criterion evidence gate unresolved.

## Acceptance criteria

1. An eligible candidate proves PASS even if discovery is otherwise incomplete.
2. A complete set of excluded candidates, including a complete empty set, returns FAIL with `NO_ELIGIBLE_COURIER`.
3. An incomplete/unavailable set or unresolved candidate cannot become FAIL.
4. Malformed status, duplicate candidate IDs, and noncanonical/missing reasons are rejected.
5. The result can feed the E7.1 gate but cannot make that gate ready without the other criteria.
6. No candidate discovery, courier activation/availability query, persistence, endpoint, Promise, Mission, Stage, or deployment is added.

## Out of scope

Courier registration and approval (E9), live availability/location, candidate generation, policy evaluation, other E7.1 criteria, operational orchestration, outcome evaluation, Promise, Mission creation, APIs, Stage, and deployment.
