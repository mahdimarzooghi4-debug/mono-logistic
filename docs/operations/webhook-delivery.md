# Webhook delivery operations

## Enablement

The worker is disabled unless `Mono:WebhookDelivery:Enabled` is explicitly set to `true`. Endpoint registration and secret rotation also require `Mono:WebhookEncryptionKey`, a base64-encoded random 32-byte AES key. Use a different key in every environment and store it in the environment's secret manager. Never use a development key in Stage or Production.

Optional worker settings:

| Setting | Default | Allowed range |
| --- | ---: | ---: |
| `Mono:WebhookDelivery:PollIntervalSeconds` | 10 | 1–300 |
| `Mono:WebhookDelivery:BatchSize` | 20 | 1–100 |

Keep delivery disabled until the environment has applied database migration 056, configured the encryption key, and has an approved outbound-network policy. Egress must allow direct HTTPS to port 443. The implementation does not use an HTTP proxy.

## Request and retry behavior

- The worker sends only supported event types whose active endpoint subscribed before the event was created.
- The request body is a customer-safe envelope built from an explicit field allowlist. It does not forward raw Outbox JSON, actor IDs, decision IDs, reason codes, or policy evidence.
- Each delivery keeps the same `X-Mono-Event-Id` and `X-Mono-Delivery-Id` across retries so receiver systems can deduplicate.
- `X-Mono-Signature` is `sha256=` followed by lowercase hex HMAC-SHA256 of the exact UTF-8 request body. The endpoint's current signing secret is used for each attempt.
- Only 2xx responses complete successfully. Network failures, request timeouts, HTTP 408, HTTP 429, and 5xx responses are retried for at most five attempts with 30 seconds, 2 minutes, 10 minutes, and 30 minutes between attempts. Other HTTP responses are dead-lettered.
- Requests have a 20-second timeout. A two-minute database lease recovers attempts after a worker process exits unexpectedly. Duplicate delivery is possible when a receiver accepts a request but the sender loses the response; consumers must deduplicate by event ID.
- Disabling an endpoint cancels pending and scheduled retries. An already in-flight request may finish.

## Network safety

Callback URLs are HTTPS on port 443. Automatic redirects and system proxies are disabled. Each new connection resolves the hostname, rejects the entire response if any resolved address is non-public, and connects directly to one of the validated addresses; normal TLS hostname and certificate validation remain in effect. This limits DNS rebinding between validation and socket connection. DNS answers and connection attempts are not logged with callback payloads or signing secrets.

## Monitoring and recovery

Inspect `webhook_deliveries` for `PENDING`, `RETRYING`, `IN_PROGRESS`, `SUCCEEDED`, `DEAD_LETTERED`, or `CANCELLED` state, and `webhook_delivery_attempt_events` for append-only attempt history. Expired leases are rescheduled automatically while attempts remain; after the fifth attempt, the delivery is dead-lettered. Administrators can replay a delivery with `POST /operations/v1/webhook-deliveries/{deliveryId}/replay` and an `Idempotency-Key`; manual SQL changes to delivery state or attempt history are not an approved recovery path.


### Worker status

Administrators can inspect the in-process sender state with `GET /operations/v1/webhook-deliveries/worker-status`. The no-store response reports whether delivery is enabled, whether this worker is `disabled`, `blocked`, `starting`, `healthy`, or `degraded`, the latest attempt/success timestamps, and the most recent claimed batch size. A worker configured on but missing secret protection reports `blocked` without exposing key material. A failed batch reports `degraded` until a later successful batch. The status does not affect API readiness and is process-local; query each API instance in a multi-instance deployment.
