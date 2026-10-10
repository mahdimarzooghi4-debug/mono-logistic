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

## Inspect customer branches

`GET /operations/v1/customers/{customerId}/branches`

The route requires Mono administrator authentication with issuer-asserted MFA. It returns branch summaries ordered by name and branch ID for the requested existing customer, including each branch's current status. An existing customer with no branches receives an empty `branches` array. Unknown customer IDs return `404 CUSTOMER_NOT_FOUND`; malformed customer IDs return `400 INVALID_REQUEST`. This read view does not change branch state or append audit events.

## Replace branch details

`PUT /operations/v1/customers/{customerId}/branches/{branchId}`

The request uses the same fields as branch registration, with a required `name` and `reason`. This is a full replacement of editable details: omitted or null optional fields clear the stored value. The customer and branch IDs must match the existing ownership relationship, and both records must remain active. The branch ID and status are immutable through this operation. The response is `200 OK` with branch metadata.

The update and its `UPDATED` audit entry are committed in one transaction. Invalid coordinates, non-object operating-hours data, or invalid text leave the branch and audit history unchanged.

## Inspect branch details

`GET /operations/v1/customers/{customerId}/branches/{branchId}`

Returns the current branch metadata for the specified customer and branch, including the stored operating-hours JSON object. The route is read-only and requires Mono administrator authentication with issuer-asserted MFA. A missing branch or a branch owned by another customer returns `404 BRANCH_NOT_FOUND`.

## Inspect branch audit history

`GET /operations/v1/customers/{customerId}/branches/{branchId}/audit?page_size=50&cursor=boa_...`

Returns branch creation and update audit records in chronological order. The default page size is 50; accepted values are 1–100. When another page exists, `nextCursor` contains the last audit ID in the current page; pass it as `cursor` to continue. The cursor must belong to the same customer and branch. Unknown or cross-branch cursors return `400 INVALID_CURSOR`; a missing or cross-customer branch returns `404 BRANCH_NOT_FOUND`.

## Manage external branch identifiers

- `GET /operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers` lists active mappings for the customer-owned branch.
- `POST /operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers` adds a mapping:

```json
{
  "namespace": "erp.branch",
  "externalId": "store-001",
  "reason": "Verified branch mapping"
}
```

A namespace names an external source or integration account. Mono trims and lowercases it; it must be 1–64 characters and contain lowercase letters, digits, dots, underscores, and hyphens. External IDs are trimmed, case-sensitive, and limited to 256 characters. Within one customer, an active namespace and external ID pair can map to only one branch. The same pair may exist for another customer because customer external systems can use overlapping identifiers.

- `POST /operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers/{identifierId}/retire` retires a mapping and requires a reason.
- `GET /operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers/audit` returns append-only add/retire history, including the administrator, reason, and timestamp.

These routes require Mono administrator authentication with issuer-asserted MFA. Retired mappings are omitted from the active list; their history remains. A retired pair can be assigned to another branch within the same customer. Identifiers can only be added to an active branch of an active customer. Unknown or cross-customer branches return `404 BRANCH_NOT_FOUND`.

## Errors

- `400 INVALID_REQUEST`: invalid branch, identifier, coordinates, operating-hours shape, or reason.
- `400 INVALID_CURSOR`: audit cursor is malformed or does not belong to the specified branch.
- `404 CUSTOMER_NOT_FOUND`: customer ID is unknown.
- `404 BRANCH_NOT_FOUND`: branch ID is unknown for the specified customer.
- `404 BRANCH_EXTERNAL_IDENTIFIER_NOT_FOUND`: identifier is unknown for the specified branch.
- `409 BRANCH_EXTERNAL_IDENTIFIER_CONFLICT`: an active namespace/external ID pair is assigned to a branch, or the mapping is already retired.
- `409 BRANCH_CONFLICT`: the branch or owning customer is not active.
- `409 CUSTOMER_CONFLICT`: customer exists but is not active.
- `401` and `403`: missing/invalid administrator authentication or insufficient role.
