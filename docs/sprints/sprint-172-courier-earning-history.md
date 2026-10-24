# Sprint 172 — Courier earning history

## Goal

Give an authenticated courier a safe, paginated history of earning ledger entries to complement the available-balance endpoint.

## Contract

- Expose `GET /courier/v1/finance/earnings` behind the existing courier authorization policy.
- Return mission ID, courier earning amount in Toman, and the ledger transaction's occurrence timestamp.
- Support a default page size of 50 and a maximum of 100; reject values outside 1–100.
- Use an opaque cursor over the append-only ledger entry sequence, newest first, and reject malformed cursors.
- Read only `COURIER_AVAILABLE` credits for the authenticated courier and `MISSION_EARNING` transactions; never accept a courier ID from the request.
- Add PostgreSQL integration coverage for ordering, amounts, and continuation across pages.

## Boundaries

- This is a read-only accrual history. It does not create withdrawal requests, reserve funds, set payout destinations, initiate transfers, or claim settlement.
- Withdrawal limits and payout state remain separate work tied to approved settlement and destination-verification policy.

## Verification

- The ledger integration test verifies two delivered Mission earnings can be read newest-first and paged without duplicates or omissions.
- Exact-head CI validates build, migration safety, PostgreSQL tests, API health, and container image.
