# Sprint 047 — Hana MVP Capacity Rule

- Status: In progress
- Phase: B — Logistics Core
- Epic: E6 — Zone & Capacity
- Capability: E6.4 — Hana MVP Capacity Rule
- Branch: sprint-047-hana-capacity-target
- Base: sprint-046-zone-capacity

## Sprint goal

Implement the approved Hana Phase-1 registered capacity target formula for the shared Zone courier pool.

## Scope

- Calculate active Hana stores × 10 using checked integer arithmetic.
- Reject negative active-store counts.
- Allow zero active stores and return zero.
- Document that the result is a shared-pool planning target, not per-store or per-Zone allocation.
- Keep the source of active-store data and online/effective capacity calculation outside this rule.

## Acceptance criteria

1. Eight active stores produce a registered capacity target of 80.
2. Zero active stores produce a target of zero.
3. Negative counts are rejected.
4. Arithmetic overflow is detected.
5. No target allocation, active-store query, courier approval, capacity persistence, zone health, promise, Stage, or deployment is added.

## Out of scope

Store activation lookup, zone allocation, operational courier registration, online/effective count calculations, target enforcement, capacity health, APIs, Stage, and deployment.
