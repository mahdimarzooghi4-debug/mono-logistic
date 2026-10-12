# Sprint 035 — Hard Policy Evaluation

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E2 — Policy Engine
- Branch: `sprint-035-hard-policy-evaluation`
- Base: `sprint-034-effective-policy-resolution`

## Sprint goal

Add an MFA-gated operations preview that evaluates the documented HARD policy class over supplied normalized facts and returns pass/fail, reason codes, and candidate exclusion.

## Scope

- Resolve active and effective policy versions using the Sprint 034 resolver.
- Evaluate only `HARD` policies; report other enforcement classes as ignored.
- Support the explicit v1 constraint format documented in `docs/operations/policy-authoring-api.md`.
- Fail closed for missing facts, malformed definitions, and unsupported operators or types.
- Keep evaluation preview separate from delivery-request intake, Mission decisions, optimization, and financial effects.
- Add deterministic evaluator tests.

## Acceptance criteria

1. Passing hard constraints return `passed=true`; a failed constraint returns its configured reason code and excludes the candidate.
2. Missing facts and unsupported policy definitions fail closed with machine-readable reason codes.
3. Non-HARD policies are never evaluated by this endpoint.
4. Policy IDs and version numbers are returned for each evaluated hard policy.
5. The endpoint requires an MFA-gated Mono administrator.

## Out of scope

Applying policy decisions to delivery requests or Missions, optimization scoring, decision persistence, Stage, and deployment.
