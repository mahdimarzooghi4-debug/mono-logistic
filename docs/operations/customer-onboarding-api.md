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

## List customers

`GET /operations/v1/customers?status=PENDING&search=mono&page_size=50&cursor=cus_...`

Returns customer registry summaries ordered by `createdAt` descending, then `customerId` descending. The optional `status` filter accepts `PENDING` or `ACTIVE` (case-insensitive); without it, all customers are included. The optional `search` value matches a case-insensitive prefix of customer ID, legal name, or display name, after trimming; it must contain 1–128 characters. `%` and `_` are treated as literal characters. The default page size is 50; accepted values are 1–100. When another page exists, `nextCursor` contains the last customer ID in the current page. Pass it as `cursor` to continue. The cursor is scoped to an existing customer record; malformed or unknown cursors return `400 INVALID_CURSOR`. The response contains registry metadata only and does not include credentials or audit history.

This route requires Mono administrator authentication with issuer-asserted MFA.

## Manage external customer identifiers

- `GET /operations/v1/customers/{customerId}/external-identifiers` lists active external identifiers for a customer.
- `POST /operations/v1/customers/{customerId}/external-identifiers` adds a mapping:

```json
{
  "namespace": "hana",
  "externalId": "merchant-001",
  "reason": "Verified customer account mapping"
}
```

A namespace identifies the source or integration account. It is trimmed and stored in lowercase, must be 1–64 characters, and may contain lowercase letters, digits, dots, underscores, and hyphens. An external ID is trimmed, case-sensitive, and limited to 256 characters. An active `namespace + externalId` pair can map to one Mono customer at a time. Duplicate active mappings return `409 EXTERNAL_IDENTIFIER_CONFLICT`.

- `POST /operations/v1/customers/{customerId}/external-identifiers/{identifierId}/retire` retires a mapping and requires a non-empty reason.
- `GET /operations/v1/customers/{customerId}/external-identifiers/audit` returns the append-only add/retire history, including administrator, reason, and timestamp.

These routes require Mono administrator authentication with issuer-asserted MFA. Retired mappings disappear from the active list; their history remains. A retired external ID can be assigned again in the same namespace, including to another Mono customer. Unknown customers return `404 CUSTOMER_NOT_FOUND`; an unknown identifier during retirement returns `404 EXTERNAL_IDENTIFIER_NOT_FOUND`.

## Inspect a customer

- `GET /operations/v1/customers/{customerId}` returns the customer registry summary, including current status.
- `GET /operations/v1/customers/{customerId}/audit` returns the append-only `CREATED` and `ACTIVATED` history in chronological order, with administrator ID, reason, and timestamp.

Both read routes require Mono administrator authentication with issuer-asserted MFA. They are customer-scoped, return `404 CUSTOMER_NOT_FOUND` for unknown IDs, and do not mutate customer or audit data.

## Errors

- `400 INVALID_REQUEST`: missing/invalid customer fields, ID, reason, page size, status filter, or search value.
- `400 INVALID_CURSOR`: malformed or unknown customer-list cursor.
- `409 EXTERNAL_IDENTIFIER_CONFLICT`: an active namespace/external ID pair is already assigned, or the mapping is already retired.
- `404 CUSTOMER_NOT_FOUND`: customer ID is unknown.
- `409 CUSTOMER_CONFLICT`: customer is not in `PENDING` status.
- `401` and `403`: missing/invalid administrator authentication or insufficient role.
