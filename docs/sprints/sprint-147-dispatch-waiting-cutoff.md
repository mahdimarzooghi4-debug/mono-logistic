# Sprint 147 — Dispatch waiting cutoff

## Goal

Help Mono operations find searching Missions that have been waiting in the dispatch queue since before a chosen UTC cutoff.

## Change

- Add optional `created_before` to `GET /operations/v1/dispatch/queue`.
- Use Mission `created_at` and an inclusive comparison.
- Apply the same cutoff during cursor validation and both page queries.
- Keep customer scoping consistent across cursor validation and every page query.

## Acceptance

- A Mission created exactly at the cutoff is included.
- Missions created after the cutoff are excluded.
- A cursor outside the cutoff is rejected.
- A filtered next page cannot return a Mission belonging to another customer.

## Verification

- Extend PostgreSQL integration coverage for the inclusive waiting cutoff, cursor validity, and customer-scoped pagination.
- GitHub Actions runs build, database integration tests, and container image checks.

## Out of scope

- Automatic prioritization or dispatch.
- Changes to Mission state.
- Stage deployment.
