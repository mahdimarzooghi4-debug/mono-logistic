# Sprint 040 — Eligible Modes Preview

- Status: In progress
- Phase: B — Logistics Core
- Epic: E4 — Shipment & Capability Model
- Branch: `sprint-040-eligible-modes-preview`
- Base: `sprint-039-mode-capability-evaluation`

## Sprint goal

Expose a read-only operations preview that returns which mode identifiers pass the effective HARD policies for a request context and its non-mode facts.

## Scope

- Build candidate mode identifiers from effective HARD MODE_CAPABILITY policy definitions.
- Evaluate each distinct candidate against every applicable HARD policy.
- Return eligible modes, candidate-level exclusion reasons, and overall reason codes.
- Fail closed when no mode capability policy exists or a policy definition is malformed.
- Expose the preview behind Mono administrator authorization.

## Acceptance criteria

1. Effective mode catalogs provide the candidate modes.
2. A mode is eligible only when it passes every applicable HARD policy.
3. Multiple effective mode catalogs are intersected through candidate evaluation.
4. Missing catalogs and malformed definitions return no eligible modes with an explicit reason.
5. The preview does not create or change requests, Missions, policies, or runtime availability.

## Out of scope

Courier/vehicle availability, capacity matching, selecting a single mode, feasibility, delivery intake, Mission creation, Stage, and deployment.
