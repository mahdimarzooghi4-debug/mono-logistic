# Sprint 022 — Managed API Key Revocation

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E16 — B2B API / E20 — Security
- Branch: `sprint-022-managed-api-key-revocation`
- Base: `sprint-021-operations-branch-inspection`

## Sprint goal

Allow Mono administrators to revoke an exposed or retired customer API key immediately, with an auditable reason.

## Scope

- Add an administrator-only operations route scoped by customer ID and key ID.
- Validate customer and key identifiers and require a non-empty reason.
- Update only a key belonging to the customer in the route.
- Return the same not-found response for an unknown key and a key owned by another customer.
- Reject repeat revocation with a conflict response.
- Store the revocation timestamp and append an immutable audit event atomically.
- Never return or persist the raw API key in this flow.
- Verify the revoked credential fails authentication after the operation.

## Acceptance criteria

1. Missing credentials receive 401 and non-administrators receive 403.
2. A Mono administrator can immediately revoke a customer's key.
3. Cross-customer attempts return 404 and do not change key state.
4. Repeated revocation returns 409 and does not append a second audit event.
5. The audit row records key, customer, administrator, reason, and timestamp.
6. Authentication rejects the revoked key.
7. CI build, tests, and container image build pass.

## Out of scope

Key issuance/rotation, customer self-service key management, changes to key scopes or expiry policy, Stage, and deployment.
