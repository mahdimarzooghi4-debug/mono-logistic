# Sprint 179 — Historical payout integrity preflight

## Goal

Prevent the payout integrity rollout from silently accepting historical ledger rows that violate the settlement rules.

## Contract

- Scan every existing `WITHDRAWAL_PAYOUT` before rollout completes.
- Require an existing withdrawal, latest operator-recorded `PAID` event with bank reference, and exactly two matching TOMAN entries.
- Install the reusable preflight function in one migration and execute it in the following migration.
- Abort the integrity check with the offending transaction ID when any legacy payout is inconsistent, while retaining the function for remediation and retry.

## Validation

- PostgreSQL integration coverage creates a balanced but amount-mismatched legacy payout under the pre-invariant schema and verifies the migration fails closed with its integrity error.
- The valid empty-schema rollout and current payout lifecycle remain covered.
- Exact-head CI validates build, migration checks, API smoke test, PostgreSQL suite, and container image build.
- PR remains Draft/Open/Unmerged for stage QA.
