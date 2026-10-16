# Sprint 174 — Courier withdrawal history audit

## Goal

Give Mono operations an auditable view of each manual payout's state changes so finance can verify the request, processing decision, and recorded bank outcome.

## Contract

- Add MFA-gated `GET /operations/v1/courier-withdrawals/{withdrawalId}/history`.
- Return immutable withdrawal events in ascending sequence: action, actor, occurrence time, decision reason, bank reference, and idempotency key.
- Write an append-only read audit for each valid withdrawal ID lookup, including not-found lookups, in the same database transaction as the read.
- Return `Cache-Control: no-store`; never return payout IBAN or encrypted payout data.
- Preserve courier ownership and payout lifecycle rules from Sprint 173.

## Validation

- PostgreSQL integration coverage verifies REQUESTED → PROCESSING → PAID history, operator attribution, bank reference, and found/not-found read-audit rows.
- Exact-head CI validates build, migration reruns, API health checks, tests, and container image construction.
- PR remains Draft/Open/Unmerged for stage QA.
