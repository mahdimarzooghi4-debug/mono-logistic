# Sprint 014 — Operations JWT Verification Tests

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E20 — Security
- Branch: `sprint-014-operations-jwt-verification-tests`
- Base: `sprint-013-operations-authentication`

## Sprint goal

Verify the Mono operations JWT boundary end-to-end without depending on a real identity provider.

## Scope

- Use an in-process test web host and a local static OIDC configuration containing a test signing key.
- Send locally signed access tokens through the real JwtBearer handler and operations authorization policy.
- Verify valid admin/MFA access and rejection for invalid signature, issuer, audience, expiration, or missing MFA.
- Verify that missing authority/audience settings reject presented operations tokens.

## Acceptance criteria

1. A valid token with the trusted signing key, configured issuer/audience, unique subject, MONO_ADMIN and MFA claim reaches the protected test route.
2. Incorrect signature, issuer, audience, expired token, or missing MFA returns 401.
3. With no identity provider settings, a presented bearer token is rejected.
4. Tests make no outbound calls and do not use production credentials.
5. CI builds and all tests pass.

## Out of scope

Real OIDC provider selection, staff provisioning, operations endpoints/UI, Stage, deployment, and production configuration.
