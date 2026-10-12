# Sprint 058 — Atomic Promise Commit

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.3 — Promise
- Branch: sprint-058-promise-atomic-commit
- Base: sprint-057-promise-commit-contract

## Scope

- Bind a commit proposal to Mono Promise and Delivery Request IDs.
- Persist tenant-scoped current Promise state and immutable version snapshots.
- Enforce initial version 1 and consecutive revisions under a delivery-request row lock.
- Persist the Promise version and `promise.committed` / `promise.revised` outbox envelope in the same transaction.
- Return deterministic idempotent replay for the same decision and identical proposal; return conflict for mismatches.

## Acceptance criteria

1. A first commit stores version 1 and one matching outbox event atomically.
2. A revision increments version, retains prior evidence, and emits `promise.revised`.
3. Identical decision replay returns its original event and makes no duplicate records.
4. Reused decision IDs with changed data and incorrect version sequences return conflict.
5. Customer scoping prevents a tenant from committing against another customer's request.
6. A failed write rolls back current state, history, and outbox together.

## Out of scope

Feasibility source verification, safety-buffer calculation, authorization policy, endpoint/API, Mission creation, outbox delivery worker, Stage, and deployment.
