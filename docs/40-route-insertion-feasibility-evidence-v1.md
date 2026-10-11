# Route Insertion Feasibility Evidence v1

## Purpose

Represent a request-wide route insertion evaluation as one of the E7.1 criterion results, once a simulator or other approved evaluator supplies it.

## Contract

The evidence envelope contains an evaluation identifier, evaluation time, completeness, explicit feasibility disposition, and canonical reason codes.

- Complete FEASIBLE evidence with no reasons returns PASS.
- Complete INFEASIBLE evidence requires reasons and returns FAIL with ROUTE_INSERTION_NOT_FEASIBLE.
- Incomplete or unavailable evidence must be UNRESOLVED with at least one reason.
- Missing evidence returns UNRESOLVED.

## Boundary

This evaluator validates the envelope and maps its explicit request-wide disposition; it does not run a route simulator, verify source trust or the scope of the caller's evaluation, define insertion positions, detour or ETA limits, capacity rules, Promise validation, route lifecycle, persistence, APIs, Mission, or Promise creation. A future simulator must establish that its disposition covers the complete request-level decision before supplying complete evidence.
