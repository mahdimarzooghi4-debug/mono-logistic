# Sprint 044 — Zone Model

- Status: In progress
- Phase: B — Logistics Core
- Epic: E6 — Zone & Capacity
- Capability: E6.1 — Zone Model
- Branch: sprint-044-zone-model
- Base: sprint-043-courier-location-foundation

## Sprint goal

Implement and validate a geometry-neutral City → Operating Zone → Micro-zone hierarchy without inventing geometry, activation, coverage, or capacity behavior.

## Scope

- Define City, Operating Zone, and Micro-zone kinds.
- Require unique zone identifiers and names.
- Require City roots, Operating Zone → City parents, and Micro-zone → Operating Zone parents.
- Keep geometry references, status labels, and capacity policy identifiers opaque and optional.
- Document prerequisites for enforcing geographic hard boundaries.

## Acceptance criteria

1. Valid three-level hierarchy passes validation.
2. City nodes cannot have parents.
3. Operating Zones require City parents; Micro-zones require Operating Zone parents.
4. Parent references must exist in the same validated registry.
5. Duplicate IDs, missing required values, and unknown kinds are rejected.
6. Geometry, status, and capacity-policy metadata are preserved without interpretation.
7. No geographic membership, activation workflow, persistence, capacity calculation, or courier assignment is added.

## Out of scope

Geometry representation or point-in-polygon rules, zone lifecycle transitions, operations API, persistence, coverage/eligibility, capacity, courier approval, Stage, and deployment.
