# Sprint 161 — Customer webhook endpoint registry

## Goal

Let an API customer register an HTTPS callback and select supported event types under an explicit `webhooks.manage` scope.

## Implementation

- Add `POST /v1/webhook-endpoints` with tenant and scope authorization.
- Validate HTTPS URLs, reject credentials/query strings/fragments, IP literals, local-only hostnames, unsupported events, duplicates, and empty subscriptions.
- Generate a one-time signing secret and persist only AES-256-GCM authenticated ciphertext. The application key is supplied as `Mono:WebhookEncryptionKey` (base64 encoded, exactly 32 bytes); registration fails closed if absent or invalid.
- Persist active endpoint metadata in PostgreSQL and add the migration to the test migration resource set.

## Boundary

This slice registers endpoints only. It does not send callbacks, resolve DNS at connection time, manage endpoint listing/disablement, rotate signing keys, or add retry policy. The delivery worker must enforce public-address resolution and redirect restrictions before opening outbound connections. Staging must configure a separate random encryption key before endpoint registration is enabled.

## Acceptance

- A caller without `webhooks.manage` cannot register an endpoint.
- Invalid/local targets and unsupported events fail before persistence.
- Raw signing secrets are returned once and never stored in plaintext.
- Missing secret-protection configuration returns service unavailable without writing a row.
