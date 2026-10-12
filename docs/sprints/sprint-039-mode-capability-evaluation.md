# Sprint 039 — Mode Capability Evaluation

- Status: In progress
- Phase: B — Logistics Core
- Epic: E4 — Shipment & Capability Model
- Branch: `sprint-039-mode-capability-evaluation`
- Base: `sprint-038-mode-capability-matrix`

## Sprint goal

Use effective MODE_CAPABILITY policies during the existing HARD policy preview to check a candidate execution mode.

## Scope

- Evaluate the candidate `mode` fact against the policy's `modes` list.
- Pass candidates whose canonical mode is listed.
- Exclude candidates whose mode is absent with `MODE_NOT_SUPPORTED`.
- Fail closed for missing mode facts or malformed historical policy definitions.
- Keep this evaluation in the administrator preview path; do not connect it to delivery intake.

## Acceptance criteria

1. A listed candidate mode passes the MODE_CAPABILITY hard policy.
2. An unlisted candidate mode is excluded with `MODE_NOT_SUPPORTED`.
3. Missing mode facts fail closed with `POLICY_FACT_MISSING`.
4. Invalid fact types and invalid policy definitions fail closed with `POLICY_DEFINITION_UNSUPPORTED`.
5. Multiple effective mode policies are independently evaluated, so a candidate must satisfy all applicable HARD policies.
6. No customer-specific logic, mode activation, availability lookup, Mission creation, or delivery intake behavior is introduced.

## Out of scope

Live courier/vehicle availability, capacity and shipment compatibility rules, mode selection, feasibility, Mission lifecycle, Stage, and deployment.
