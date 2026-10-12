# Sprint 111 — Courier read access audit

## Goal

Make access to the Mono courier roster and operational detail accountable before the product expands into personal identity records or document workflows.

## Scope

- Record privileged courier roster reads, detail reads, and audit-ledger inspections with staff actor, action, Courier ID where applicable, result count, and server timestamp.
- Persist audit entries in PostgreSQL with append-only protections against update, delete, and truncate.
- Add an MFA-gated, bounded keyset-paginated operations route for audit-ledger inspection.
- Fail closed: if the audit write fails, do not return the roster, detail, or inspected audit page.
- Keep the audit event free of search terms, personal data, and document contents.

## Acceptance criteria

- The roster read creates a COURIER_SEARCH event with the returned result count.
- A valid detail lookup creates a COURIER_DETAIL event, including a zero-result event for a valid but missing Courier ID.
- Audit ledger inspection creates a COURIER_AUDIT_INSPECTION entry.
- Read audit records cannot be updated, deleted, or truncated through database operations.
- The endpoint requires the existing MFA-gated Mono administrator policy and limits pages to 100 events.
- PostgreSQL integration tests verify event contents and append-only behavior.

## Data boundary

Audit rows contain only staff actor ID, action, internal Courier ID where applicable, result count, and occurrence time. They do not contain roster filters, names, phone numbers, identity data, evidence references, or document content.

## Operational boundary

This Sprint does not add courier self-authentication, identity-document collection or verification, availability, dispatch eligibility, OSM/GPS, Stage, or deployment.

## Validation

- PostgreSQL integration coverage runs against an isolated schema with migrations 033 and 034.
- Full CI runs .NET build/tests and container image build.
