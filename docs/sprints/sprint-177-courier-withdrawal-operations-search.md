# Sprint 177 — Courier withdrawal operations search

## Goal

Give finance operations a traceable queue across the full withdrawal lifecycle, including paid and rejected requests, without changing payout decisions or accounting rules.

## Contract

- Add MFA-gated `GET /operations/v1/courier-withdrawals/search`.
- Support optional exact status (`REQUESTED`, `PROCESSING`, `PAID`, `REJECTED`) and courier ID filters.
- Preserve newest-first keyset pagination and bind cursors to the active filter values.
- Record every search in an immutable audit table with operator identity, filters, cursor, page size, result count, and timestamp.
- Return only the masked destination IBAN and the existing withdrawal summary fields; do not expose destination secrets.

## Validation

- PostgreSQL coverage verifies search across requested, rejected, and paid items, cursor continuation without omission, filter selection, and audit records.
- Exact-head CI validates build, repeatable migrations, API health, tests, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
