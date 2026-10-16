# Route Planning Feasibility Gate v1

## Goal

Compose the three route planning criteria with the remaining E7.1 criterion results and run the existing eight-criterion feasibility gate.

## Contract

- The caller supplies the expected Zone, route capacity snapshot, positive minimum effective-courier requirement, active-route inventory, route-insertion evaluation, and any other criterion results.
- The expected Zone must resolve in a structurally valid caller-supplied Zone registry. An absent/unknown Zone or registry remains `UNRESOLVED` for both Zone criteria. Capacity, active-route inventory, and route-insertion evidence are accepted only when they identify the same expected Zone; missing/mismatched Zone evidence remains `UNRESOLVED` and evidence is never combined across Zones.
- Capacity is evaluated by `EffectiveZoneCapacityCriterion`; active routes by `ActiveRouteInventoryCriterion`; route insertion by `RouteInsertionFeasibilityCriterion`.
- The caller supplies the other E7.1 criteria exactly as results. Route-owned criteria cannot be supplied twice or override the composed results.
- The composed eight results are evaluated by `FeasibilityEvidenceGate`, preserving its canonical criterion order and block/unresolved behavior.

## Boundaries

This module composes typed evidence only. It does not query or verify evidence sources, impose freshness thresholds, derive an effective-capacity requirement, calculate courier shipment fit, infer route status, search alternative Promise times, or create/commit a Promise. Invalid Zone registries and malformed capacity snapshots are rejected; freshness policy remains unselected. The existing policy leaves the capacity threshold and freshness rules to separate approved decisions.
