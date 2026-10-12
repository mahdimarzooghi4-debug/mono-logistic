# Feasibility Evidence Gate v1

## Purpose

Prevent partial E7.1 evidence from being treated as ready for delivery outcome evaluation.

## Required criteria

The gate requires exactly one result for each criterion:

1. Coverage
2. Distance
3. Eligible modes
4. Eligible couriers
5. Capacity
6. Active routes
7. Route insertion
8. SLA feasibility

Each criterion reports PASS, FAIL, or UNRESOLVED. A failed or unresolved criterion must include at least one canonical reason code. Duplicate and unknown criteria are invalid.

## Gate status

- READY_FOR_OUTCOME_EVALUATION: every required criterion has an explicit PASS result.
- BLOCKED: at least one criterion has a known FAIL result.
- UNRESOLVED: no known failure exists, but at least one criterion is missing or unresolved.

A known failure blocks even when other criteria are missing. The gate returns criteria in canonical order and aggregates distinct reason codes.

## Decision boundary

The gate does not produce ACCEPTED, ALTERNATIVE_PROMISE, or REJECTED; it does not create a Mission, commit a Promise, persist evidence, or call the existing mode-policy preview. Existing previews and the distance criterion must be connected by a future runtime feasibility orchestration layer before this gate can be used operationally.
