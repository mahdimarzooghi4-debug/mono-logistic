# Sprint 034 — Effective Policy Resolution

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E2 — Policy Engine
- Branch: `sprint-034-effective-policy-resolution`
- Base: `sprint-033-policy-lifecycle`

## Sprint goal

Return the active policy versions that apply to a supplied service context with stable scope precedence.

## Scope

- Match platform, service, customer, contract, branch, zone, mission, and runtime scopes to explicit context identifiers.
- Filter to Active versions whose effective dates contain the requested time.
- Order results by scope tier, descending priority, then stable policy ID.
- Require MFA-gated Mono administrator access for the operations preview endpoint.
- Add PostgreSQL-backed tests for scope matching, effective dates, ordering, and access control.

## Acceptance criteria

1. Platform policies always match; scoped policies require a matching context identifier.
2. Inactive, Draft, Retired, future, and expired versions are excluded.
3. Results are deterministic and include policy identity, version, definition, enforcement type, and precedence tier.
4. Resolution does not evaluate policy conditions or apply decisions to delivery requests or Missions.

## Out of scope

Policy merging, family-specific validation, conflict evaluation, runtime decision execution, financial posting, Stage, and deployment.
