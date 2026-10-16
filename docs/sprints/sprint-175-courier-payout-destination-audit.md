# Sprint 175 — Courier payout destination audit history

## Goal

Let finance operations review which payout IBAN was manually verified, who accessed its full value, and when, without exposing the full IBAN in history responses.

## Contract

- Add MFA-gated `GET /operations/v1/couriers/{courierId}/payout-destination/audit` with bounded keyset pagination.
- Combine immutable destination verification/revocation events with full-IBAN access audit outcomes in one chronological stream.
- Return only masked IBANs; include opaque verification evidence references and actor/time details.
- Bind cursors to the requested courier and reject malformed or cross-courier cursors.
- Append an immutable audit row for every valid-courier read in the same transaction, including not-found lookups.

## Validation

- PostgreSQL integration coverage verifies event ordering and pagination across verification and secret-access records, masking, and read-audit outcomes.
- Exact-head CI validates build, migration reruns, API health checks, tests, and container image construction.
- PR remains Draft/Open/Unmerged for stage QA.
