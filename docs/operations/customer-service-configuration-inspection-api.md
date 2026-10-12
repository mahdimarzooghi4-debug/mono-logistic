# Customer Service Configuration Inspection API

These routes are reserved for Mono operations staff. Both require a valid token from the dedicated `MonoOperationsJwt` authentication scheme and the `MONO_ADMIN` role. Customer API keys do not authorize these operations routes.

## Version summaries

`GET /operations/v1/customers/{customerId}/services/{serviceCode}/configurations?limit=50`

Returns at most 100 versions ordered by descending version number. The response includes configuration ID, customer ID, service code, version, status, effective dates, and creation timestamp. It does not include the stored configuration JSON.

## Audit history

`GET /operations/v1/customers/{customerId}/services/{serviceCode}/configuration-audit?limit=50`

Returns at most 100 append-only audit entries, newest first. Entries include action, actor ID, reason, resource/version identifiers, and timestamp.

## Input and errors

- `customerId` must match the opaque Mono customer ID format.
- `serviceCode` is case-normalized and must match Mono's service-code format.
- `limit` is optional, defaults to 50, and must be from 1 through 100.
- Invalid identifiers or limits return `400 INVALID_REQUEST`.
- Missing/invalid authentication returns `401`; an authenticated non-administrator receives `403`.

The API is read-only. Configuration payload authoring and activation are not exposed through these routes. No Hana-specific configuration is seeded.
