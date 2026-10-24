# Sprint 176 — Courier financial ledger history

## Goal

Let a courier view all posted movements against their available-balance ledger in one history, including both mission earnings and paid withdrawal debits.

## Contract

- Expose authenticated `GET /courier/v1/finance/ledger` with a default page size of 50 and maximum of 100.
- Read only the `COURIER_AVAILABLE` account belonging to the courier identity from the validated authentication context.
- Return the ledger entry ID, transaction reference type/ID, direction, absolute amount in Toman, and transaction occurrence time.
- Use the immutable ledger entry sequence for newest-first keyset pagination.
- Include only posted ledger entries; pending withdrawal reservations remain visible through the existing withdrawal request history and are not labeled as settled ledger movements.

## Validation

- PostgreSQL integration coverage verifies a paid withdrawal debit and mission earning credit appear newest-first across cursor pages with their correct references and amounts.
- Exact-head CI validates build, migration safety, PostgreSQL tests, API health, and container image.
- PR remains Draft/Open/Unmerged for stage QA.
