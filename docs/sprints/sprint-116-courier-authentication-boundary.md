# Sprint 116 — Courier authentication boundary

## Goal

Establish a distinct, provider-neutral JWT authentication boundary for courier self-service APIs, separate from Mono staff and customer API-key identities.

## Scope

- Add a dedicated OIDC/JWT bearer scheme configured through paired HTTPS Authority and Audience values.
- Require a unique `sub`, unique Mono-assigned `mono/courier_id`, and exactly one `mono/role = COURIER` claim.
- Reject tokens containing a reserved Mono identity-domain claim before validation.
- On every token validation, confirm the courier still exists, has trust status ACTIVE, and is not operationally suspended.
- Add an authenticated, minimal `GET /courier/v1/session` endpoint exposing only the caller's internal Courier ID.
- Keep courier identity context separate from Mono operations and customer API identities.

## Acceptance criteria

- Missing Authority/Audience configuration leaves the courier scheme fail-closed; partial configuration or a non-HTTPS Authority fails application setup.
- Signature, issuer, audience, and token lifetime are validated by JwtBearer.
- Duplicate/missing subject, courier ID, or role claims are rejected.
- Operations/customer identities cannot become courier identities by presenting role claims alone.
- Inactive, unknown, or suspended couriers are rejected against current PostgreSQL state, even if their JWT has not expired.
- The session route requires the dedicated courier authorization policy and returns no PII, location, financial, or trust-history data.
- Tests cover active acceptance, suspended/inactive rejection, duplicate subject rejection, and isolated context extraction.

## Identity-provider boundary

The configured trusted issuer must issue `mono/courier_id` only after it has completed and bound the required verified courier identity. Mono's authority, audience, mobile-verification flow, OTP delivery provider, and account-provisioning workflow are not configured or claimed complete by this sprint. No real provider or courier credential is enabled.

## Boundaries

This is authentication plumbing only. It does not issue tokens, verify mobile numbers, store identity documents, ingest GPS, or authorize assignment/dispatch. The endpoint stays unavailable until a trusted issuer is configured.

No Stage or production deployment is included.

## Validation

The exact branch head must pass .NET build, PostgreSQL-backed integration tests, and container-image CI.
