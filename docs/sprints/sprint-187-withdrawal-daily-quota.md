# Sprint 187 — Daily withdrawal quota visibility

## Goal

Show couriers how much of the current daily withdrawal request allowance remains.

## Contract

- Add authenticated `GET /courier/v1/finance/withdrawal-quota`.
- Return the existing daily limit, count of requests in the current Asia/Tehran calendar day, remaining slots, and next local-midnight reset as UTC.
- Count all persisted requests once, regardless of current withdrawal status; idempotent retries do not add rows.
- Treat this as advisory; the transactional withdrawal request endpoint remains authoritative under concurrency.
- Apply `Cache-Control: no-store`.

## Validation

- PostgreSQL integration coverage verifies a full quota, reset at the Tehran day boundary, and the following reset timestamp.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
