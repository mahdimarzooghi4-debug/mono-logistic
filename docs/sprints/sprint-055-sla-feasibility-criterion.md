# Sprint 055 — SLA Feasibility Criterion

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1 — SLA feasibility
- Branch: sprint-055-sla-feasibility-criterion
- Base: sprint-054-route-insertion-evidence

## Sprint goal

Evaluate a caller-supplied estimated delivery time against an explicit SLA deadline without inventing a duration or deriving an estimate.

## Scope

- Model an SLA evidence envelope with evaluation ID/time, completeness, estimated delivery time, latest allowed delivery time, and reason codes.
- Return PASS when estimate is at or before deadline and FAIL when it is later.
- Keep missing, incomplete, or unavailable evidence UNRESOLVED.
- Validate evidence IDs, timestamps, completeness, and canonical reason codes.
- Verify that the SLA criterion alone cannot satisfy the aggregate E7.1 gate.
- Document dependencies and decision boundaries.

## Acceptance criteria

1. Estimate before or equal to deadline returns PASS.
2. Estimate after deadline returns FAIL with a stable reason code.
3. Missing estimate or deadline, missing evidence, and incomplete/unavailable sources return UNRESOLVED.
4. Malformed evidence is rejected.
5. Results feed the aggregate gate without satisfying other required criteria.
6. No SLA duration, estimate algorithm, policy selection, route simulation, Mission, Promise, API, Stage, or deployment is introduced.

## Out of scope

SLA target selection, customer policy resolution, route and ETA simulation, Ready Time scheduling, existing Mission promise validation, feasibility outcome evaluation, Mission/Promise creation, persistence, APIs, Stage, and deployment.
