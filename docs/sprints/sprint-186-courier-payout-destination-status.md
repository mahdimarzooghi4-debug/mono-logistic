# Sprint 186 — Courier payout destination status

## Goal

Let a courier confirm which payout destination is currently verified before requesting a withdrawal.

## Contract

- Add authenticated `GET /courier/v1/finance/payout-destination`, keyed only by the authenticated courier context.
- Return `VERIFIED` with the masked IBAN, or `NOT_VERIFIED` with a null masked IBAN.
- Mark the response `Cache-Control: no-store`.
- Do not expose a full IBAN, destination ID, verification evidence, or destination write operations to courier APIs.

## Validation

- PostgreSQL coverage checks an existing verified destination and the not-verified response for another courier identity.
- Exact-head CI runs migration validation, build, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
