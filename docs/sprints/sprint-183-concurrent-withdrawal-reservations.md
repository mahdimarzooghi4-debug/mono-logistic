# Sprint 183 — Concurrent withdrawal reservations

## Goal

Prove that simultaneous withdrawal requests for one courier cannot reserve the same available funds twice.

## Contract

- Start two requests for the same courier concurrently, each individually affordable but unaffordable in combination.
- Exactly one request is accepted; the other receives `INSUFFICIENT_AVAILABLE_BALANCE`.
- The accepted request remains idempotently replayable and conflicting input with its key is rejected.
- Preserve the existing per-courier row lock and transaction boundary; do not change withdrawal policy.

## Validation

- PostgreSQL integration test runs both writes concurrently against a courier with a 44,500 toman posted balance.
- Verify one 30,000 toman reservation and 14,500 toman remaining availability.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
