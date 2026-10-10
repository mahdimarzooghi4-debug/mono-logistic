# Sprint 025 — Managed API Key Audit Inspection

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E16 — B2B API / E20 — Security
- Branch: `sprint-025-api-key-audit-inspection`
- Base: `sprint-024-customer-lifecycle-inspection`

## Sprint goal

Let Mono administrators inspect the immutable revocation history for a customer's managed API key without exposing credential secrets.

## Scope

- Add a customer- and key-scoped read route for API key operations audit history.
- Require the existing MFA-gated Mono administrator policy.
- Return action, actor, reason, and occurrence time in deterministic order.
- Return an empty list for an existing key without audit events.
- Return the same not-found response for an unknown key and a key outside the requested customer's scope.
- Keep key and audit records unchanged; never return a raw key or stored hash.
- Add integration coverage for authorization, empty and revoked histories, and customer isolation.

## Acceptance criteria

1. Unauthenticated requests receive 401 and non-administrators receive 403.
2. Existing keys return their audit history, including revocation actor and reason.
3. Existing keys without events return an empty list.
4. Unknown or cross-customer keys return 404 without leaking ownership.
5. Reads do not append or modify audit events.
6. CI build, tests, and container image build pass.

## Out of scope

Key issuance or rotation, revocation policy changes, customer-facing routes, Stage, and deployment.
