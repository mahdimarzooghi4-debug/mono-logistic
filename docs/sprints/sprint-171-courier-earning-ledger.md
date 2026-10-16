# Sprint 171 — Courier earning ledger

## Goal

Implement the first production-shaped Finance backend slice from the approved Hana Phase 1 earning rule: a delivered Mission posts a balanced, immutable ledger transaction and the assigned courier can read the resulting available balance.

## Contract

- Use the retained quote snapshot as the gross Mission fare; do not recalculate price during settlement.
- On `mission.delivered`, use the completed accepted courier offer and post one `TOMAN` transaction in the Mission lifecycle database transaction.
- Allocate the approved 10% Mono commission and 10% VAT on that commission; the remaining 89% is credited to the courier's available balance.
- Record customer receivable as the balancing debit and credit courier available, Mono commission revenue, and VAT payable.
- Reject unbalanced ledger transactions at commit. Ledger accounts, transactions, and entries are append-only; corrections require future compensating entries.
- Expose only the authenticated courier's derived available balance at `GET /courier/v1/finance/balance`.

## Boundaries

- This package records accruals only. It does not initiate bank transfers, create withdrawal requests, set payout destinations, or claim that a payment has settled.
- Approved MVP fares are integer Toman amounts divisible by 100, so the currently approved commission and VAT allocations are exact. Other fare precision is rejected pending an approved rounding rule.
- Existing minimal lifecycle test schemas without quote and courier-offer tables do not create financial entries. A fully migrated delivered Mission must have both the retained quote and a completed courier assignment or the transition rolls back.

## Verification

- PostgreSQL integration coverage verifies the 50,000 Toman example (44,500 courier, 5,000 commission, 500 VAT), balanced entries, duplicate posting protection, derived balance, immutable entries, and migration rerun safety.
