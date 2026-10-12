# Sprint 036 — Optimization Policy Inputs

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E2 — Policy Engine
- Branch: `sprint-036-optimization-policy-inputs`
- Base: `sprint-035-hard-policy-evaluation`

## Sprint goal

Define and validate configurable optimization weights, priorities, and scoring-model inputs without hard-coding customer preferences or selecting runtime candidates.

## Scope

- Accept `WEIGHTED_SUM` optimization definitions containing one or more supported criteria.
- Validate criterion names, non-negative decimal weights, and positive integer priorities when creating optimization drafts and activating them.
- Preserve the authored configuration in existing policy inspection and effective-resolution responses.
- Reject malformed and unknown scoring inputs.
- Keep optimization downstream of hard-policy evaluation; no optimizer or delivery decision path is added.
- Add pure validation tests.

## Supported criteria

`eta`, `detour`, `route_density`, `pickup_fit`, `capacity_balance`, and `cost`.

## Acceptance criteria

1. An optimization policy can store a valid `WEIGHTED_SUM` configuration with a subset of the supported criteria.
2. Unknown/duplicate criteria, negative or invalid weights, missing priorities, and unsupported scoring models are rejected.
3. Existing policy inspection retains the exact configuration and version evidence.
4. This sprint does not calculate scores, compare candidates, override HARD policies, or apply decisions to delivery requests or Missions.

## Out of scope

Runtime metric normalization, score aggregation semantics beyond the declared model identifier, candidate ranking/selection, decision persistence, Stage, and deployment.
