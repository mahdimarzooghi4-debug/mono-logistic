# Sprint 006 — Managed API Key Provisioning

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer Configuration / E16 — B2B API / E20 — Security
- Branch: `sprint-006-managed-api-key-provisioning`
- Base: `sprint-005-customer-branch-api`

## Sprint goal

Provide a controlled internal workflow for issuing the first scoped B2B API credentials without exposing a public key-issuance endpoint.

## Scope

- Add a command-line provisioner for existing ACTIVE Mono customers.
- Generate cryptographically random key IDs and secrets in the existing ADR 026 format.
- Store only the SHA-256 hash of each complete key.
- Support only scopes currently registered in Mono's scope catalog.
- Require an explicit expiry and interactive confirmation.
- Reveal the raw key only after the database insert succeeds.
- Test key format, scope validation, database persistence, and inactive-customer rejection.

## Acceptance criteria

1. Invalid customer IDs, unknown scopes, empty scopes, and expired timestamps are rejected before storage.
2. The database stores the key ID, customer ID, expiry, and SHA-256 hash, never the raw key.
3. Provisioning is possible only for an existing ACTIVE customer.
4. Cancellation or failure returns without revealing the raw API key.
5. A stored key validates through the same credential verifier used by API requests.
6. No public issuance endpoint, staging, deployment, or production change is added.

## Operational handling

The operator must run this command from a trusted environment with database access and copy the one-time output directly into the approved secret store. Do not paste it into source control, tickets, chat, or build logs.

## Out of scope

- Customer creation or branch writes.
- Credential revocation commands and operational audit integration.
- Hana service adapter or delivery endpoints.
