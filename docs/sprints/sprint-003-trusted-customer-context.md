# Sprint 003 — Trusted Customer Context

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-003-trusted-customer-context`
- Base: `sprint-002-customer-branch-foundation`

## Sprint goal

Establish a provider-neutral authorization boundary for customer-scoped operations. Resolve tenant identity only from an authenticated principal and make tenant scoping difficult to bypass in repository calls.

## Scope

- Define Mono's internal customer ID claim contract.
- Resolve a customer context only from an authenticated principal with exactly one valid customer claim.
- Add a fail-closed customer-tenant authorization policy.
- Change branch repository reads to derive the tenant from the trusted context.
- Reject branch writes whose customer does not match that context.
- Test unauthenticated, missing, malformed, ambiguous, and cross-tenant cases.

## Acceptance criteria

1. Request bodies and arbitrary method arguments cannot select the customer scope for branch reads.
2. Unauthenticated principals and principals with missing, malformed, or multiple customer claims cannot obtain a customer context.
3. Branch lookup uses both the authenticated customer ID and branch ID.
4. Branch writes fail before database access when the branch customer differs from the authenticated context.
5. The authorization policy requires an authenticated identity and valid Mono customer context.
6. No token issuer, API key format, signing key, or authentication provider is selected or embedded.
7. No public business endpoint is exposed by this sprint.

## Out of scope

- OAuth2 / API-key issuance and validation, external identity provider selection, customer user management, roles beyond tenant membership, endpoint exposure, staging/deployment.
