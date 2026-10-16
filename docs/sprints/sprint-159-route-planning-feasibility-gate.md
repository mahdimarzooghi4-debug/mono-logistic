# Sprint 159 — Route planning feasibility gate composition

## Goal

Connect the existing route-insertion, active-route inventory, and effective Zone-capacity contracts to the eight-criterion E7.1 gate without weakening missing-evidence behavior or making new capacity/freshness policy.

## Implementation

- Add a domain composition module that evaluates capacity, active-route inventory, and route insertion evidence, then combines them with supplied coverage, distance, eligible-mode, eligible-courier, and SLA criterion results.
- Require the Zone registry, capacity snapshot, active-route inventory, and route-insertion evidence to match the caller's explicit planning Zone; missing or mismatched Zone evidence remains unresolved.
- Delegate the final disposition and canonical ordering to `FeasibilityEvidenceGate`.
- Reject route-owned criteria in the caller's `OtherCriteria` collection to prevent duplicate or substituted results.

## Scope boundary

This is evidence composition. It does not establish source trust, freshness, capacity threshold, courier fit, route activity rules, alternative Promise search, or Promise commitment. Those decisions remain with their approved owners and upstream evidence producers.

## Acceptance

- All eight passing criteria produce `READY_FOR_OUTCOME_EVALUATION`.
- A known effective-capacity failure blocks the gate even if other evidence is missing.
- Missing evidence and cross-Zone snapshots cannot pass.
- Existing criterion and outcome semantics remain unchanged.
