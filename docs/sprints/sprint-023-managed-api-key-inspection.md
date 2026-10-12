# Sprint 023 — Managed API Key Inspection

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E16 — B2B API / E20 — Security
- Branch: `sprint-023-managed-api-key-inspection`
- Base: `sprint-022-managed-api-key-revocation`

## Sprint goal

Let Mono administrators inspect customer API key metadata before rotation or revocation without exposing any credential secret.

## Scope

- Add an administrator-only customer-scoped read endpoint.
- Return key ID, customer ID, scopes, creation time, expiry, and revocation time.
- Never return the raw key or stored hash.
- Return an empty list for an existing customer with no keys and 404 for an unknown customer.
- Use a deterministic ordering for results.
- Keep key issuance and revocation behavior unchanged.
- Cover authorization, empty results, scope, metadata, and secret exclusion.

## Acceptance criteria

1. Missing credentials receive 401 and non-administrators receive 403.
2. A Mono administrator sees only metadata for the requested customer.
3. The response contains no raw key or secret hash.
4. Known customers with no keys receive an empty list; unknown customers return 404.
5. Revocation timestamps are visible after revocation.
6. CI build, tests, and container image build pass.

## Out of scope

Key issuance, mutation, pagination, changes to scope or expiry policy, Hana-specific data, Stage, and deployment.
