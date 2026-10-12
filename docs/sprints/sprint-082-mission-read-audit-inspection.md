# Sprint 082 — Mission read audit inspection

## Goal

Give Mono administrators a bounded way to investigate privileged Mission access.

## Changes

- Add an MFA-gated read endpoint for Mission read audit records.
- Support keyset pagination plus optional Mission ID, actor ID, and inclusive occurrence-time filters.
- Bind cursors to every active filter and keep the audit response limited to actor, action, Mission ID, result count, and timestamp.
- Record audit-ledger inspections in the same append-only ledger.
- Add the supporting index, migration, PostgreSQL tests, and API/backlog documentation.

## Security and scope

The existing administrator policy with verified MFA is required. Audit queries persist no arbitrary search text. Ledger records remain append-only. No Mission state changes, Stage, or deployment are included.

## Validation

CI will run the full build, PostgreSQL-backed API suite, and container image build on this Draft PR.
