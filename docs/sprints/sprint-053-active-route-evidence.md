# Sprint 053 — Active Route Inventory Evidence

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1 — Active routes evidence
- Branch: sprint-053-active-route-evidence
- Base: sprint-052-effective-zone-capacity-criterion

## Sprint goal

Require complete and known per-Zone route inventory evidence before route-insertion feasibility can rely on the active-route criterion.

## Scope

- Model a Zone route inventory with an observation time and COMPLETE, INCOMPLETE, or UNAVAILABLE source status.
- Record each route's activity as ACTIVE, INACTIVE, or UNRESOLVED.
- Pass only when the source is complete and every supplied route has known activity.
- Keep empty complete inventory as valid evidence; do not treat it as a delivery rejection.
- Validate route identifiers, duplicate entries, statuses, and reason codes.
- Confirm that this criterion alone cannot ready the aggregate E7.1 gate.

## Acceptance criteria

1. Complete inventory with all route states known returns PASS, including an empty inventory.
2. Incomplete/unavailable inventory or any unresolved route returns UNRESOLVED with reason evidence.
3. Duplicate route IDs, invalid statuses, malformed timestamps/Zone IDs, and malformed reason codes are rejected.
4. Incomplete inventory cannot be accepted as complete evidence.
5. The result feeds the E7.1 gate without satisfying other missing criteria.
6. No route lifecycle, freshness policy, persistence, simulation, API, Mission, Promise, Stage, or deployment is introduced.

## Out of scope

Route model and lifecycle (E11), trusted route source, durable storage, active-route queries, freshness threshold, route insertion/simulation, SLA calculation, outcome evaluation, Mission and Promise creation, APIs, Stage, and deployment.
