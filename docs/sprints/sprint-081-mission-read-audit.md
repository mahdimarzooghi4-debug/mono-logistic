# Sprint 081 — Mission operations read audit

## Goal

Make cross-customer Mission inspection accountable while keeping the read API privacy-limited.

## Changes

- Record the authenticated Mono administrator for each successful Mission search, detail read, and timeline read.
- Store action, requested Mission ID when applicable, page result count, and timestamp in a dedicated append-only table.
- Do not persist search terms, customer-provided external references, customer IDs, or event payloads in the audit record.
- Fail the read request when its required audit write fails.
- Add PostgreSQL coverage for action scope, staff attribution, result counts, admin-only recording, and mutation rejection.

## Security and scope

The existing MFA-gated administrator policy remains required. The record is written after a successful database read and before returning its response. A missing Mission detail or timeline is recorded with a zero result count. No Mission state transitions, Stage, or deployment are included.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and container image build on this Draft PR.
