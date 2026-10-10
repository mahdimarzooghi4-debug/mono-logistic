# Customer Service Configuration Authoring API

These write routes are reserved for Mono operations administrators. Both require the dedicated `MonoOperationsJwt` scheme, `MONO_ADMIN` role, and the issuer-asserted MFA claim.

## Create a Draft

`POST /operations/v1/customers/{customerId}/services/{serviceCode}/configurations`

Request body:

```json
{
  "effectiveFrom": "2026-10-10T12:00:00Z",
  "effectiveTo": null,
  "configuration": {},
  "reason": "Change request reference"
}
```

Creates the next version in `DRAFT` status and appends a `DRAFT_CREATED` audit entry. The response returns the configuration ID, customer/service, version, status, effective dates, and creation time. It omits the configuration payload.

## Activate a Draft

`POST /operations/v1/customers/{customerId}/service-configurations/{serviceConfigurationId}/activate`

Request body:

```json
{
  "reason": "Approval reference"
}
```

Activation uses the existing transactional overlap check and appends an `ACTIVATED` audit entry. It returns `204 No Content` on success.

## Retire an active configuration

`POST /operations/v1/customers/{customerId}/service-configurations/{serviceConfigurationId}/retire`

Request body:

```json
{
  "reason": "Service discontinued by operations"
}
```

Only an `ACTIVE` version can be retired. Retirement changes its status to `RETIRED` and appends a `RETIRED` audit entry in the same transaction. It takes effect immediately; Mono will fail closed if no other active version is effective. The endpoint returns `204 No Content`.

## Errors and boundaries

- `400 INVALID_REQUEST`: malformed/invalid identifiers, dates, JSON object, reason, or request binding.
- `404 CONFIGURATION_NOT_FOUND`: no matching Draft exists for the supplied customer and configuration ID.
- `409 CONFIGURATION_CONFLICT`: the record is not a Draft or its effective interval conflicts with an active version.
- `401`: missing or invalid operations authentication.
- `403`: authenticated identity lacks the administrator role.
- Customer API keys cannot authorize these routes.
- No customer-specific seed configuration is created by these endpoints; configuration values must be explicitly submitted by an authorized Mono administrator.
