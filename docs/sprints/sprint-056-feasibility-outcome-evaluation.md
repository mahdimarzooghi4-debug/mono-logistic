# Sprint 056 — Feasibility Outcome Evaluation

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.2 — Results
- Branch: sprint-056-feasibility-outcome-evaluation
- Base: sprint-055-sla-feasibility-criterion

## Approved rule

1. Accept the requested time only when every E7.1 criterion passes.
2. If the requested time is blocked, evaluate an explicitly supplied alternative.
3. Return an alternative promise only when the proposed alternative passes every E7.1 criterion.
4. Reject only when a complete alternative search reports no feasible alternative.
5. Missing, incomplete, unavailable, or inconclusive evidence remains unresolved.

## Scope

- Add a typed outcome result with resolved/unresolved state.
- Evaluate complete requested E7.1 criteria against the requested delivery time.
- Accept the requested time only when the aggregate gate is ready.
- Map a complete feasible alternative with all E7.1 criteria passing to ALTERNATIVE_PROMISE.
- Map a complete negative alternative search to REJECTED.
- Keep unresolved requests and alternative searches from producing final outcomes.
- Validate IDs, timestamps, enums, reason codes, status combinations, and complete criterion evidence.
- Do not select/search/rank alternatives or commit a Promise.

## Acceptance criteria

1. Requested time with all eight PASS criteria returns ACCEPTED.
2. Blocked requested time plus complete feasible alternative evidence returns ALTERNATIVE_PROMISE.
3. REJECTED is returned only after a complete search finds no feasible alternative.
4. Incomplete or missing requested/alternative evidence returns UNRESOLVED.
5. Alternative promises require a different proposed time and all eight E7.1 criteria to PASS.
6. The evaluator does not persist decisions, commit promises, create missions, or make API calls.

## Out of scope

Alternative time generation/ranking, ETA and route simulation, SLA policy selection, trusted search provenance, persistence, audit decision record, Mission creation, Promise commit, operational APIs, Stage, and deployment.
