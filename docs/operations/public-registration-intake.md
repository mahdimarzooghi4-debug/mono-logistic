# Public registration intake

The public landing can submit a business or courier cooperation request to `POST /v1/public-registration-requests`. A successful submission creates only a `SUBMITTED` application request; it does not create a customer account, branch, courier profile, or login.

## Required server configuration

- `Mono:PublicRegistration:PrivacyNoticeVersion`: exact version shown and accepted by the person.
- `Mono:PublicRegistration:EncryptionKey`: base64-encoded 32-byte AES-256 key. Keep this in the deployment secret store and do not commit it.

When either value is absent or invalid, the endpoint returns 503 before writing data. Consent must be explicit and the submitted notice version must equal the configured version. The payload (name, phone, optional business name) is AES-256-GCM encrypted in PostgreSQL. Idempotency keys and request fingerprints are HMAC digests; the raw key is not persisted.

## Endpoints

- `POST /v1/public-registration-requests`: public, requires `Idempotency-Key`, `consent: true`, and the configured privacy notice version.
- `GET /operations/v1/public-registration-requests?page_size=50`: operations administrator with issuer-verified MFA; decrypts only the active queue and records queue access.
- `POST /operations/v1/public-registration-requests/{requestId}/transition`: operations administrator with MFA; supports `CONTACT_ATTEMPTED`, `DECLINED` (reason code required), and `CLOSED`.

Status changes are audit-recorded. Open requests expire after 90 days. Closed or declined requests expire 30 days after closure, capped at 90 days from submission. The hosted retention worker deletes expired requests and their transition events hourly.

The landing UI remains disconnected until product supplies the actual privacy notice URL/content and version, so the user can review the notice before giving consent. Configure ingress-level rate limiting and request-size limits before exposing the public endpoint to production traffic.
