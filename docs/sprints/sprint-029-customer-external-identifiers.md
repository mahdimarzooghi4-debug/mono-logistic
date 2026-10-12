# Sprint 029 — Customer External Identifiers

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration
- Branch: `sprint-029-customer-external-identifiers`
- Base: `sprint-028-customer-registry-search`

## Sprint goal

Associate Mono customer records with identifiers assigned by outside systems without adding Hana-specific fields to the customer core.

## Scope

- Add and list external customer identifiers through MFA-gated Mono operations routes.
- Use a normalized namespace and preserve external ID case.
- Ensure an active namespace/external ID pair maps to one Mono customer at a time.
- Retire mappings instead of deleting them and preserve an append-only audit history.
- Allow a retired external ID to be assigned again while retaining the old record and audit events.
- Add PostgreSQL constraints, integration coverage, and API documentation.

## Acceptance criteria

1. Unauthenticated and non-administrator requests are rejected.
2. Administrators can add and list identifiers for an existing customer.
3. Duplicate active mappings in a namespace return `409 EXTERNAL_IDENTIFIER_CONFLICT`.
4. Retiring a mapping and its audit event are committed atomically.
5. Retired mappings are omitted from the active list and remain in audit history.
6. A retired external ID can be assigned to another customer in the same namespace.
7. CI build, tests, and container image build pass.

## Out of scope

Automatic external-system synchronization, customer-facing identifier routes, Stage, and deployment.
