# Mono Operations Authentication Boundary

The API has a dedicated `MonoOperationsJwt` bearer scheme for future internal Mono operations endpoints. It is separate from customer API-key authentication.

## Configuration

Set both values in a trusted runtime configuration source:

- `MonoOperations:Authority`: HTTPS OIDC authority URL.
- `MonoOperations:Audience`: exact audience issued for the Mono API.

If both are absent, the scheme rejects every operations token. If only one is present, or the authority is not HTTPS, application configuration is invalid.

## Required signed claims

The trusted issuer must issue:

- `iss`, `aud`, `exp`, and `sub`: validated by JWT bearer middleware.
- `mono/role`: role names, including `MONO_ADMIN` for administrators.
- `mono/mfa=true`: issuer-asserted MFA completion for administrator access.

After signature/issuer/audience/lifetime validation, the API adds a reserved `mono/identity_domain=operations` marker. Tokens containing that reserved claim are rejected. Operations services accept only a validated identity with exactly one `sub`.

Do not expose the `MONO_ADMIN` claim directly from user-editable profile data. The OIDC provider and its MFA claim-mapping policy must be selected and reviewed before enabling the scheme in an operational environment. The customer onboarding contract is documented in [Mono Customer Onboarding API](customer-onboarding-api.md). The customer service configuration read contract is documented in [Customer Service Configuration Inspection API](customer-service-configuration-inspection-api.md), and its write contract is documented in [Customer Service Configuration Authoring API](customer-service-configuration-authoring-api.md). All operations routes require the administrator policy.
