# Sprint 050 — Eligible Modes Feasibility Criterion

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1 — Eligible modes criterion
- Branch: sprint-050-eligible-modes-criterion
- Base: sprint-049-feasibility-gate

## Sprint goal

Connect the existing policy-based mode preview to the E7.1 evidence contract as one isolated feasibility criterion.

## Scope

- Adapt eligible mode preview output into a canonical E7.1 criterion result.
- Return PASS when at least one candidate mode passes effective HARD policies.
- Return FAIL when candidate evaluation is complete and every candidate is excluded.
- Return UNRESOLVED when policy or candidate evidence is incomplete.
- Reject inconsistent candidate lists, duplicate/noncanonical modes, and malformed reason codes.
- Demonstrate that this single criterion does not make the overall eight-criterion gate ready.

## Acceptance criteria

1. A non-empty eligible mode set maps to PASS.
2. Complete evaluation with no eligible modes maps to FAIL with `NO_ELIGIBLE_MODE`.
3. Missing capability policy, unsupported policy definition, missing facts, and absent candidates map to UNRESOLVED.
4. Inconsistent eligible/candidate lists and malformed evidence are rejected.
5. Existing criterion results can be submitted to the aggregate gate, which remains UNRESOLVED until all other criteria are supplied.
6. No request intake, live availability query, mode selection, persistence, Promise, Mission, Stage, or deployment behavior is added.

## Out of scope

Live vehicle/courier availability, shipment-to-mode compatibility beyond existing effective HARD policies, all other feasibility criteria, runtime orchestration, decision persistence, outcome evaluation, Promise, Mission creation, APIs, Stage, and deployment.
