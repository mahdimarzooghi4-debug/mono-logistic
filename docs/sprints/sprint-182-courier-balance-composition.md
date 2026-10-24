# Sprint 182 — Courier balance composition

## Goal

Explain the live available balance by exposing its posted ledger amount and pending withdrawal reservations alongside the spendable result.

## Contract

- Return posted courier balance, reserved amount for `REQUESTED`/`PROCESSING` withdrawals, and available balance from one SQL statement.
- Preserve `available_balance_toman` and existing authorization for backward compatibility.
- Keep reservations separate from ledger statements and posted accounting movements.

## Validation

- PostgreSQL coverage verifies the three values before payout, after payout, while another withdrawal is reserved, and after rejection releases the reservation.
- The database computes available funds as posted balance minus active reservations in the same query snapshot.
- Exact-head CI validates build, migration checks, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
