# Sprint 070 — Quote Issuance API

## Goal

Expose the approved first Quote issuance application service through an authenticated, tenant-scoped API with atomic idempotent retries.

## Scope

- Add `POST /v1/delivery-requests/{id}/quote` under `delivery-requests.write`.
- Accept explicit expiry and distance evidence; calculate the evidence digest and fare on the server.
- Require `Idempotency-Key` and bind it to a canonical request hash.
- Persist Quote version 1 and its idempotency mapping in one PostgreSQL transaction.
- Return the existing public Quote read shape for issue and replay.
- Reject key reuse with a different request and reject a second initial Quote.
- Preserve fail-closed behavior while the evidence source verifier is unconfigured.

## Acceptance

- Missing or invalid idempotency keys and malformed evidence are rejected.
- Same key and same request replay the original Quote.
- Same key with changed request conflicts.
- Concurrent requests cannot create duplicate initial Quotes.
- Tenant isolation and public response field boundaries remain intact.
- Migration 020 is embedded for database integration tests.

## Exclusions

No Stage or deployment work, source-provider trust configuration, Quote renewal, or Mission confirmation is included.
