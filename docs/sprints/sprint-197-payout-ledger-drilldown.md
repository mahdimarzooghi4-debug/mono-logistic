# Sprint 197 — Payout ledger drilldown

## Goal
Allow administrators to trace a withdrawal reconciliation result to posted accounting transactions and entries without exposing payout secrets or idempotency keys.

## Delivered
- Added GET /operations/v1/courier-payouts/{withdrawalId}/ledger-detail under administrator authorization with no-store responses.
- Returns lifecycle state and safe transaction and double-entry details.
- Distinguishes FOUND, PAYOUT_NOT_POSTED, and WITHDRAWAL_NOT_FOUND.
- Records each valid-ID read in append-only audit history, including not-found probes and result counts.
- Added integration coverage for the paid withdrawal entries and audited not-found outcome.

## Security
- Omits account owner IDs, bank references, payout destination data, and idempotency keys.
- Invalid-shaped IDs fail validation before database access.
- Read-only; reconciliation and posting behavior are unchanged.
