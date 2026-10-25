# Public registration intake

The public landing can submit a business or courier cooperation request to `POST /v1/public-registration-requests`. A successful submission creates only a `SUBMITTED` application request; it does not create a customer account, branch, courier profile, or login.

## Privacy notice and required configuration

The landing displays notice version `2026-10-25-v1` from `src/Mono.Landing/src/privacy-notice.tsx`, requires an unticked explicit-consent checkbox, and sends that exact version with each request. Keep `Mono:PublicRegistration:PrivacyNoticeVersion` set to this exact value. A material change to the notice requires a new version and coordinated frontend/backend configuration.

- `Mono:PublicRegistration:EncryptionKey`: base64-encoded 32-byte AES-256 key. Keep this in the deployment secret store and do not commit it.
- `Mono:PublicRegistration:ControllerName`: verified legal name of the data controller.
- `Mono:PublicRegistration:PrivacyContact`: verified official channel for privacy requests.
- `Mono:PublicRegistration:HostingDisclosure`: verified hosting/subprocessor providers and data-storage location disclosure.
- `VITE_MONO_PRIVACY_CONTROLLER_NAME` and `VITE_MONO_PRIVACY_CONTACT`: same verified public values rendered by the landing.
- `VITE_MONO_PRIVACY_HOSTING_DISCLOSURE`: same verified infrastructure disclosure rendered by the landing.

When any required value is absent, the encryption key is invalid, or the configured notice version does not match the submitted version, intake does not accept the request. The payload (name, phone, optional business name) is AES-256-GCM encrypted in PostgreSQL. Idempotency keys and request fingerprints are HMAC digests; the raw key is not persisted. Set `VITE_MONO_API_BASE_URL` for the landing build when the API is not served from the landing origin; configure HTTPS and the required CORS/reverse-proxy policy for that deployment.

## Endpoints

- `POST /v1/public-registration-requests`: public, requires `Idempotency-Key`, `consent: true`, and the configured privacy notice version.
- `GET /operations/v1/public-registration-requests?page_size=50`: operations administrator with issuer-verified MFA; decrypts only the active queue and records queue access.
- `POST /operations/v1/public-registration-requests/{requestId}/transition`: operations administrator with MFA; supports `CONTACT_ATTEMPTED`, `DECLINED` (reason code required), and `CLOSED`.

Status changes are audit-recorded. Open requests expire after 90 days. Closed or declined requests expire 30 days after closure, capped at 90 days from submission. The hosted retention worker deletes expired requests and their transition events hourly.

The public notice is a product draft. Before enabling real public intake, the owner must supply the operator's full legal identity, official contact channel for data-access/correction/deletion requests, and the actual hosting/subprocessor and data-location details. These facts are not available in the repository and must not be guessed. Both the API and retention worker stay disabled until all these values are configured. Configure ingress-level rate limiting and request-size limits before exposing the public endpoint to production traffic.
