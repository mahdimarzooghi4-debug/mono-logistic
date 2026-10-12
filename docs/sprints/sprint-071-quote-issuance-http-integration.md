# Sprint 071 — Quote Issuance HTTP Integration

## Goal

Verify that the public Quote issuance API, application service, tenant authorization, and PostgreSQL persistence work together as one request flow.

## Scope

- Send a write-authorized HTTP request to the Sprint 070 Quote endpoint.
- Replace the fail-closed source verifier only inside the integration test host.
- Confirm the initial Quote is returned in the customer-safe read shape.
- Retry the identical payload with the same Idempotency-Key and confirm the same Quote is returned.
- Reuse the key with changed distance evidence and confirm an idempotency conflict.
- Confirm only one Quote v1 exists after those requests.

## Acceptance

- Test executes against the CI PostgreSQL service and exercises the real endpoint, repositories, and application service.
- Same key and exact same canonical input returns the original Quote ID.
- Same key with changed input returns HTTP 409 and `IDEMPOTENCY_CONFLICT`.
- Internal distance source and attestation fields are absent from the response.
- No production verifier or trust configuration is introduced.

## Exclusions

This is test hardening for Sprint 070. No business rule, public endpoint, or production behavior changes. No Stage, deployment, or release work is included.
