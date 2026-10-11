# Sprint 052 — Effective Zone Capacity Criterion

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1 — Capacity criterion
- Branch: sprint-052-effective-zone-capacity-criterion
- Base: sprint-051-eligible-courier-evidence

## Sprint goal

Use the existing Zone Capacity Snapshot as explicit evidence for aggregate effective-courier capacity without inventing a threshold or shipment-capacity formula.

## Scope

- Compare snapshot `effective_count` with a caller-supplied positive required effective-courier count.
- Pass on equality or greater count; fail on a known shortfall.
- Return unresolved when either the snapshot or requirement is unavailable.
- Reject invalid snapshots and non-positive requirements.
- Verify that a single passing capacity criterion does not complete the E7.1 gate.

## Acceptance criteria

1. Effective count equal to or above the supplied requirement returns PASS.
2. Effective count below the supplied requirement returns FAIL with `EFFECTIVE_CAPACITY_INSUFFICIENT`.
3. Missing snapshot or requirement returns UNRESOLVED with a stable reason.
4. Negative snapshot counts and non-positive requirements are rejected.
5. The criterion can feed the E7.1 gate, which remains unresolved until the other criteria are supplied.
6. No freshness threshold, capacity conversion formula, courier assignment, Promise, Mission, endpoint, Stage, or deployment is added.

## Out of scope

Per-courier shipment weight/volume fit, deriving the required count from a request, freshness policy, snapshot aggregation/persistence, live courier availability, capacity allocation, runtime orchestration, outcome evaluation, Promise, Mission, APIs, Stage, and deployment.
