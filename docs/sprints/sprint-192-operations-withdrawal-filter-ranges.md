# Sprint 192 — Operations withdrawal search ranges

## Goal

Let finance operations narrow the privileged withdrawal search by request time and amount while retaining a complete audit of the query context.

## Contract

- Add paired from_at/to_at request-creation filters with an inclusive lower and exclusive upper bound.
- Add inclusive min_amount_toman/max_amount_toman filters; reject negative bounds and reversed intervals.
- Apply filters before keyset pagination and bind every filter to the cursor.
- Extend the append-only operations search audit to store the normalized time and amount bounds.
- Preserve existing status and courier filters and the safe response shape.

## Validation

- PostgreSQL integration coverage verifies bounded results, cursor continuation, and persisted filter values in the audit ledger.
- Migration 073 adds and constrains filter-audit fields.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
