# Mono Customer Onboarding API

These internal routes are reserved for Mono operations administrators. They require the dedicated operations JWT scheme, the `MONO_ADMIN` role, and the issuer-asserted MFA claim. Customer API keys cannot authorize these routes.

## Create a pending customer

`POST /operations/v1/customers`

```json
{
  "legalName": "Example Merchant LLC",
  "displayName": "Example",
  "customerType": "MERCHANT",
  "defaultChannel": "B2B_API",
  "defaultCurrency": "IRT",
  "reason": "Approved onboarding request reference"
}
```

Creates a new customer with a Mono-generated opaque ID and `PENDING` status. The response returns customer metadata and does not issue API credentials. Creation is recorded in the append-only customer operations audit.

## Activate a customer

`POST /operations/v1/customers/{customerId}/activate`

```json
{
  "reason": "Contract signed and onboarding verified"
}
```

Only a `PENDING` customer can be activated. The status transition to `ACTIVE` and the `ACTIVATED` audit record are committed together. Successful activation returns `204 No Content`. Credential provisioning remains a separate operational action.

## Errors

- `400 INVALID_REQUEST`: missing/invalid customer fields, ID, or reason.
- `404 CUSTOMER_NOT_FOUND`: customer ID is unknown.
- `409 CUSTOMER_CONFLICT`: customer is not in `PENDING` status.
- `401` and `403`: missing/invalid administrator authentication or insufficient role.
