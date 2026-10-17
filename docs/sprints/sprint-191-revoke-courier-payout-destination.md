# Sprint 191 — Revoke courier payout destination

## Goal

Give Mono operations a controlled way to disable a courier payout destination when its verification must be withdrawn.

## Contract

- Add an administrator-authorized, idempotent operation to revoke the courier's current verified destination.
- Require an idempotency key and a safe evidence reference; record a REVOKED event in the existing append-only destination event ledger.
- Serialize revocation with verification and withdrawal creation through the existing courier lock.
- Exact retries replay successfully; reused keys with changed commands conflict.
- A courier with no current verified destination receives a conflict outcome, and new withdrawal requests remain blocked until a destination is verified again.
- Preserve already-created withdrawal and payout history; revocation only gates future requests.

## Validation

- PostgreSQL coverage verifies revoke, replay, idempotency conflict, status change, blocked new request, and successful re-verification.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
