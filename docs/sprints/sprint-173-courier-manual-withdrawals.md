# Sprint 173 — Courier manual withdrawals

## Goal

Let couriers request on-demand withdrawals from earned balance, reserve requested funds, and let MFA-verified Mono administrators record manual bank payout outcomes.

## Approved policy

- Count a maximum of three requests per Asia/Tehran calendar day. Each successfully created request counts, including a later paid or rejected request; idempotent retries do not count again.
- A payout destination must be manually checked by Mono finance operations. The operator records an opaque evidence reference. No bank verification provider is connected.
- Hold requested funds while status is `REQUESTED` or `PROCESSING`; rejection releases the hold.
- Operator processing is manual. Marking a request `PAID` requires a bank reference and posts the payout ledger entry atomically with the immutable status event.
- No minimum withdrawal amount was specified; accept positive integer Toman amounts up to the currently available balance.

## Contract

- MFA-gated operations verification/rotation of the courier's payout IBAN.
- AES-256-GCM encrypted IBAN storage with courier ID as authenticated additional data, keyed fingerprint, and last-four-only masked reads. Raw IBAN is never returned.
- Courier-scoped, idempotent withdrawal creation and history. The authenticated courier ID is derived from the validated token.
- Row-locked per-courier creation to enforce daily request and available-balance limits under concurrent calls.
- MFA-gated operator queue and idempotent transitions through `REQUESTED`, `PROCESSING`, `PAID`, and `REJECTED`.
- Append-only destination and withdrawal event records. Paid requests create a balanced ledger payout transaction; pending requests reduce the derived available balance.
- PostgreSQL integration coverage for encrypted/masked destination persistence, idempotency, reservation, payment, rejection release, and daily limit.

## Configuration and boundaries

- Destination verification fails closed unless `MonoCourier:PayoutIbanEncryptionKey` is set to a Base64-encoded 32-byte key.
- This package does not contact a bank, initiate transfers, impose a minimum, charge fees, or claim a request is paid until an operator records the bank reference.
- Existing withdrawal requests retain the destination verified at creation; subsequent destination rotation does not redirect them.

## Verification

- Exact-head CI runs build, repeatable-migration validation, API health checks, PostgreSQL integration tests, and container image build.
