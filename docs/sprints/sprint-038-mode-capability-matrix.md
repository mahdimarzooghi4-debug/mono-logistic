# Sprint 038 — Mode Capability Matrix

- Status: In progress
- Phase: B — Logistics Core
- Epic: E4 — Shipment & Capability Model
- Branch: `sprint-038-mode-capability-matrix`
- Base: `sprint-037-shipment-facts`

## Sprint goal

Represent execution mode identifiers in a policy definition so the capability catalog can grow without changing Mission core.

## Scope

- Add validation for `MODE_CAPABILITY` policy draft definitions.
- Use a `modes` array of 1–32 unique uppercase identifiers. Example: `{ "modes": ["WALK", "BIKE", "CAR"] }`.
- Record the initial identifiers `WALK`, `BIKE`, and `CAR` in the backlog contract.
- Permit future identifiers such as `VAN`, `PICKUP`, and `TRUCK` through policy data.
- Apply the same definition validation when creating a policy and when creating a new version.

## Acceptance criteria

1. A mode capability definition contains only a non-empty `modes` array with unique canonical identifiers.
2. Initial and future mode identifiers pass the same format validation.
3. Invalid, duplicate, or non-canonical identifiers are rejected at policy draft authoring.
4. Mode capability policy validation is independent of customer identity and does not add Hana-specific branches.
5. Adding a mode identifier requires no Mission-core change.

## Out of scope

Mode selection, courier or vehicle availability, capacity limits, compatibility rules, feasibility, Mission behavior, Stage, and deployment.
