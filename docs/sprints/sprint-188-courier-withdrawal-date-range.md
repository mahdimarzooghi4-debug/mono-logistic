# Sprint 188 — Courier withdrawal history date ranges

## Goal

Help couriers find withdrawal requests from a specific period without paging through unrelated history.

## Contract

- Add paired `from_at` and `to_at` filters to the courier withdrawal list, using request creation time.
- Treat `from_at` as inclusive and `to_at` as exclusive; reject a missing pair or an empty/reversed interval.
- Bind the keyset cursor to both normalized UTC filter values so it cannot continue under a different window.
- Leave the operations queue contract and cursor unchanged.

## Validation

- PostgreSQL coverage verifies bounded list results, page continuation within the same window, and an empty window.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
