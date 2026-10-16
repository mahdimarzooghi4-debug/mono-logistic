# Sprint 184 — Tehran withdrawal day boundary

## Goal

Lock down the existing policy of at most three withdrawal requests per Tehran calendar day, including the midnight boundary.

## Contract

- A request exactly at the next Tehran local midnight belongs to the new daily window.
- Three unique requests are allowed in that window; a fourth is rejected with `DAILY_LIMIT_REACHED`.
- Replaying an accepted idempotency key returns the original request and does not consume another daily slot.
- Keep the current policy and implementation unchanged unless the integration test reveals a defect.

## Validation

- PostgreSQL integration coverage begins with three requests in the prior Tehran day, then checks three requests at the new day boundary plus replay and fourth-request rejection.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
