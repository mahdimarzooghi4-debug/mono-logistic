# Sprint 049 — Feasibility Evidence Gate

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1 — Feasibility gate
- Branch: sprint-049-feasibility-gate
- Base: sprint-048-distance-feasibility

## Sprint goal

Require evidence for every E7.1 criterion before a request may proceed to outcome evaluation, without defining the final outcome or Promise.

## Scope

- Define the eight E7.1 criteria from the product backlog.
- Accept one explicit PASS, FAIL, or UNRESOLVED result per criterion.
- Fail closed: any known failure blocks; missing or unresolved evidence leaves the gate unresolved.
- Require stable reason codes for failed and unresolved criterion results.
- Return results in canonical order and aggregate distinct reasons.
- Document that mode preview and distance evaluation are not wired to runtime orchestration.

## Acceptance criteria

1. Eight explicit PASS results return READY_FOR_OUTCOME_EVALUATION.
2. Any known FAIL returns BLOCKED, including when another criterion is missing.
3. Missing or UNRESOLVED criteria return UNRESOLVED when no known failure exists.
4. Duplicate criteria, unknown enums, invalid reason codes, and reasonless failures/unresolved results are rejected.
5. The gate does not return ACCEPTED, ALTERNATIVE_PROMISE, or REJECTED and does not create a Mission or Promise.
6. No endpoint, persistence, policy integration, Stage, or deployment is added.

## Out of scope

Runtime criteria orchestration, address and zone resolution, policy lookup, candidate generation, route simulation, Promise creation, Mission creation, APIs, Stage, and deployment.
