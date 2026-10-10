# Sprint 013 — Mono Operations Authentication Boundary

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E20 — Security / E1 — Customer, Contract & Configuration
- Branch: `sprint-013-operations-authentication`
- Base: `sprint-012-service-configuration-inspection`

## Sprint goal

Add a fail-closed, provider-neutral OIDC JWT validation boundary for internal Mono operations identities.

## Scope

- Validate signature, issuer, audience, and token lifetime using ASP.NET Core JWT bearer authentication and HTTPS metadata.
- Require both authority and audience settings together; with neither configured, reject every operations token.
- Require a unique `sub` subject and reserve the Mono operations identity marker for the validated server-side pipeline.
- Admit `MONO_ADMIN` only when a trusted issuer supplies the signed `mono/mfa=true` claim.
- Register operations authorization context and policy without mapping an operations endpoint.
- Keep customer API key authentication separate from Mono staff identity.

## Acceptance criteria

1. Missing/partial/HTTP authority configuration cannot authenticate operations identities.
2. Invalid subject, missing MFA for admin, or reserved identity marker fails closed.
3. Customer API key principals cannot satisfy the operations identity context.
4. Admin authorization policy uses the dedicated JWT bearer scheme and requires both admin role and MFA.
5. Tests cover actor uniqueness, role/MFA behavior, and identity domain separation.
6. CI builds, runs tests, and builds the API container.

## Decisions still open

- Concrete OIDC provider, issuer URL, audience value, staff enrollment, and MFA policy.
- Internal admin endpoints and UI.

## Out of scope

Provider tenant provisioning, sign-in UI, Stage, deployment, Hana seed/configuration, and production changes.
