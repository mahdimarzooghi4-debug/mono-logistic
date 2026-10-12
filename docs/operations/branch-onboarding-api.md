# Mono Branch Onboarding API

This internal route is reserved for Mono operations administrators. It requires the dedicated operations JWT scheme, the `MONO_ADMIN` role, and issuer-asserted MFA. Customer API keys cannot authorize it.

## Register a branch

`POST /operations/v1/customers/{customerId}/branches`

```json
{
  "name": "Central Branch",
  "address": "Main street",
  "latitude": 35.7,
  "longitude": 51.4,
  "zoneId": "zone-central",
  "operatingHours": {},
  "reason": "Branch onboarding approval reference"
}
```

The customer must exist and have `ACTIVE` status. Mono generates the branch ID and stores the new branch as `ACTIVE`. Coordinates are optional, but latitude and longitude must be supplied together and remain within geographic ranges. Operating hours, when supplied, must be a JSON object; the API preserves that object without imposing a schedule schema. A branch creation audit entry records the customer, branch, administrator, reason, and time in the same transaction.

## Errors

- `400 INVALID_REQUEST`: invalid name, coordinates, operating-hours shape, or reason.
- `404 CUSTOMER_NOT_FOUND`: customer ID is unknown.
- `409 CUSTOMER_CONFLICT`: customer exists but is not active.
- `401` and `403`: missing/invalid administrator authentication or insufficient role.
