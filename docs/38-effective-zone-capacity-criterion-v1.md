# Effective Zone Capacity Criterion v1

## Purpose

Evaluate the E7.1 capacity criterion against the existing Zone Capacity Snapshot.

## Contract

The evaluator compares a caller-supplied positive minimum effective-courier count with the snapshot's `effective_count`:

- `PASS` when effective count meets or exceeds the requirement.
- `FAIL` with `EFFECTIVE_CAPACITY_INSUFFICIENT` when known effective count is below the requirement.
- `UNRESOLVED` when the snapshot or required count is unavailable.
- Invalid snapshots and non-positive requirements are rejected.

## Boundary

The requirement is supplied by the caller; this contract invents no threshold or formula. The evaluator compares aggregate effective couriers in a Zone only. It does not prove a specific courier can carry the shipment, assess route insertion/SLA, or define capacity freshness. No persistence, request orchestration, Mission, or Promise is added.
