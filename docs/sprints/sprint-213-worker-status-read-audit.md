# Sprint 213 — Worker status read audit

## Goal
Record privileged reads of the webhook delivery and Mission offer-expiry worker status endpoints in the existing append-only operations audit ledgers.

## Delivered
- Add `WORKER_STATUS_READ` to the webhook operations audit ledger and searchable action filter.
- Add `MISSION_WORKER_STATUS` to the Mission operations read-audit ledger.
- Preserve the administrator-only, no-store responses and record one result for each successful status read.
- Extend PostgreSQL integration coverage for both audit ledgers.

## Verification
- GitHub Actions runs build, migration validation, API smoke tests, tests, and container validation for this draft.
