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

## Replace branch details

`PUT /operations/v1/customers/{customerId}/branches/{branchId}`

The request uses the same fields as branch registration, with a required `name` and `reason`. This is a full replacement of editable details: omitted or null optional fields clear the stored value. The customer and branch IDs must match the existing ownership relationship, and both records must remain active. The branch ID and status are immutable through this operation. The response is `200 OK` with branch metadata.

The update and its `UPDATED` audit entry are committed in one transaction. Invalid coordinates, non-object operating-hours data, or invalid text leave the branch and audit history unchanged.

## Errors

- `400 INVALID_REQUEST`: invalid name, coordinates, operating-hours shape, or reason.
- `404 CUSTOMER_NOT_FOUND`: customer ID is unknown.
- `409 CUSTOMER_CONFLICT`: customer exists but is not active.
- `401` and `403`: missing/invalid administrator authentication or insufficient role.
