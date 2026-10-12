# Hana MVP Capacity Rule v1

## Rule

For Hana Phase 1, the registered courier capacity target is:

```
registered_capacity_target = active_hana_stores × 10
```

Each active Hana store contributes 10 couriers to the shared Zone courier pool. The target is not reserved to an individual store.

The rule returns a non-negative integer target for a non-negative active-store count and uses checked arithmetic. Eight active Hana stores produce a target of 80.

## Scope boundaries

This rule calculates only the registered capacity target. It does not query or define how active stores are determined, distribute the target across Zones or stores, calculate online/effective capacity, enroll or approve couriers, enforce a threshold, or alter delivery promises. The factor 10 is the Phase-1 MVP planning assumption and may be recalibrated using operating data.
