# Sprint 048 — Distance Feasibility Criterion

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1 — Feasibility (distance criterion)
- Branch: sprint-048-distance-feasibility
- Base: sprint-047-hana-capacity-target

## Sprint goal

Add a deterministic route-distance constraint evaluator as the first isolated criterion in the broader feasibility engine.

## Scope

- Compare route distance in metres with a caller-supplied configured maximum.
- Pass a distance exactly at the limit; fail distances above it with MAX_DISTANCE_EXCEEDED.
- Return UNRESOLVED when route distance is not available.
- Reject non-finite or negative distances/limits.
- Keep Hana's 5 km value outside the evaluator; future configuration may pass 5,000 metres.

## Acceptance criteria

1. At-limit distance passes.
2. Above-limit distance fails with a stable reason code.
3. Missing route distance remains unresolved.
4. Invalid numeric inputs are rejected.
5. The check does not produce a final feasibility outcome, Promise, Mission, or price.
6. No provider calls, address resolution, policy lookup, persistence, coverage, courier, capacity, route insertion, SLA, Stage, or deployment behavior is added.

## Out of scope

Final feasibility aggregation, configured limit retrieval, Maps Gateway integration, address resolution, eligibility decisions, route simulation, Promise, pricing, Mission creation, and APIs.
