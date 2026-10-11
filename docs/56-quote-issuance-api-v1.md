# Quote Issuance API v1

## Endpoint

`POST /v1/delivery-requests/{deliveryRequestId}/quote` requires `delivery-requests.write` and exactly one valid `Idempotency-Key` header.

The request supplies an explicit `expires_at` and `distance_evidence` fields: `evidence_id`, `observed_at`, `distance_meters`, `source_id`, and `source_attestation`. The API computes the integrity digest itself. Clients cannot supply a fare, Quote ID, version, or digest.

The endpoint invokes the Quote issuance application service. It verifies the tenant-bound evidence source, calculates the fare from Hana Distance Pricing v1, and persists only Quote version 1. Successful issue and replay return the same tenant-scoped public Quote read shape.

## Retry and concurrency

The request hash binds the delivery request, normalized expiry, and all evidence fields. A retry using the same key and hash returns the original Quote. Reusing a key with a different request returns `409 IDEMPOTENCY_CONFLICT`. A different key cannot create a second initial Quote for the same delivery request.

The Quote snapshot and key record are written in one PostgreSQL transaction. A transaction-scoped advisory lock serializes concurrent use of the same tenant key; the delivery request row lock serializes attempts to issue its first Quote. Tenant and foreign-key constraints remain in force.

## Fail-closed source verification

The default Hana evidence verifier remains unconfigured. Until Mono approves and registers a source-specific verifier, a structurally valid request receives `422 QUOTE_EVIDENCE_UNVERIFIED` and no Quote is issued.
