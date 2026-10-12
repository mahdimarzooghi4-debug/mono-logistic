# SLA Feasibility Criterion v1

## Purpose

Evaluate whether a supplied estimated delivery time meets an explicitly supplied latest-allowed delivery time for E7.1.

## Contract

The evidence contains an evaluation identifier and time, completeness, an estimated delivery time, a latest-allowed delivery time, and reason codes.

- PASS when the estimated delivery time is earlier than or equal to the explicit SLA deadline.
- FAIL with SLA_DEADLINE_MISSED when the estimate is later than the deadline.
- UNRESOLVED when the evidence is missing, incomplete, unavailable, or either comparison timestamp is absent.
- Invalid identifiers, timestamps, statuses, or reason codes are rejected.

## Boundary

The caller supplies the applicable SLA deadline and estimated delivery time. This criterion invents no target duration or deadline, does not select the policy, calculate the estimate, verify source trust, simulate a route, account for pickup readiness or existing Mission promises, make an outcome decision, or create a Mission or Promise. Those inputs and orchestration require later work.
