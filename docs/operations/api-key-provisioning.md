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

Only scopes in the source-controlled scope catalog are accepted. The catalog contains `branches.read` and `delivery-requests.write`; grant only the scopes needed by each integration.

Do not run the command in CI, capture its output in build logs, or paste the key into source control, tickets, chat, or unapproved storage. Use HTTPS when sending the value to Hana.

## Failure behavior

Invalid arguments, unsupported scopes, missing/inactive customers, and database failures do not reveal the raw API key. The customer must exist and have status `ACTIVE` before a key can be issued.
