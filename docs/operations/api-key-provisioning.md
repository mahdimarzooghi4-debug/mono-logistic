# Internal API Key Provisioning

The provisioner issues a per-customer credential for an existing ACTIVE Mono customer. It does not create customers or expose an HTTP key-issuance route.

## Requirements

- Run from a trusted operations terminal with access to Mono's PostgreSQL database.
- Set `ConnectionStrings__Mono` to the protected database connection string.
- Use an approved secret store for the one-time key output.
- Apply migrations `001_customer_branch.sql`, `002_customer_api_keys.sql`, and `003_delivery_request_intake.sql` before using delivery request scopes.

## Issue a credential

```sh
dotnet run --project tools/Mono.ApiKeyProvisioner -- \
  --customer-id <ACTIVE_CUSTOMER_ID> \
  --scope branches.read \
  --expires-at <ISO-8601_TIMESTAMP>
```

The command displays the customer, scopes, and expiry and requires typing `ISSUE`. It prints the complete API key only after PostgreSQL confirms the insert. The database stores only the SHA-256 hash of the complete key. Copy the value directly to the approved secret store; it cannot be retrieved later.

Only scopes in the source-controlled scope catalog are accepted. The catalog contains `branches.read`, `delivery-requests.write`, and `delivery-requests.read`; grant only the scopes needed by each integration.

Do not run the command in CI, capture its output in build logs, or paste the key into source control, tickets, chat, or unapproved storage. Use HTTPS when sending the value to Hana.

## Inspect credentials

`GET /operations/v1/customers/{customerId}/api-keys`

This read-only route requires Mono administrator authentication with issuer-asserted MFA. It returns each credential's key ID, customer ID, scopes, creation time, expiry, and revocation time in stable order. A known customer with no credentials receives an empty `apiKeys` array; an unknown customer returns `404 CUSTOMER_NOT_FOUND`. The response never contains the raw key or its stored hash. Key validity still depends on the credential expiry, revocation state, and customer status.

## Inspect credential audit history

`GET /operations/v1/customers/{customerId}/api-keys/{keyId}/audit`

This read-only route requires Mono administrator authentication with issuer-asserted MFA. It returns the selected customer's key audit events in chronological order, including action, administrator ID, reason, and timestamp. An existing key with no audit events receives an empty `entries` array; an unknown key or a key belonging to another customer returns `404 API_KEY_NOT_FOUND`. The response contains no credential secret or stored hash and does not change key or audit data.

## Revoke a credential

`POST /operations/v1/customers/{customerId}/api-keys/{keyId}/revoke`

This internal route requires Mono administrator authentication with issuer-asserted MFA and a non-empty reason. Revocation is immediate: the credential's `revoked_at` value and an immutable audit entry are committed in one transaction. The raw key is never returned or stored by this operation. Unknown keys and keys belonging to another customer return the same `404 API_KEY_NOT_FOUND`; attempting to revoke an already-revoked key returns `409 API_KEY_CONFLICT`.

## Failure behavior

Invalid arguments, unsupported scopes, missing/inactive customers, and database failures do not reveal the raw API key. The customer must exist and have status `ACTIVE` before a key can be issued.
