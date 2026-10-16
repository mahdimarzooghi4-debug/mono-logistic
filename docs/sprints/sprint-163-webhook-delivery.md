# Sprint 163 — Secure Outbox webhook delivery

## Goal

Deliver subscribed customer-safe Outbox events reliably while preventing callback requests from reaching private or redirected destinations.

## Scope

- Fan out supported Outbox events to active endpoints created before each event.
- Build versioned JSON envelopes using per-event allowlists instead of forwarding raw Outbox payloads.
- Sign the exact request bytes with the endpoint's AES-GCM-protected secret and HMAC-SHA256.
- Connect only to HTTPS port 443 through a direct socket that validates every DNS answer as public; preserve ordinary TLS hostname/certificate validation.
- Disable proxies and automatic redirects.
- Persist durable per-endpoint delivery state, bounded retries, lease recovery, terminal dead-letter state, and append-only attempt events.
- Keep the background worker disabled unless explicitly configured and key material is present.

## Retry policy

- Up to five send attempts.
- Retry network errors, timeouts, HTTP 408, HTTP 429, and 5xx responses after 30 seconds, 2 minutes, 10 minutes, and 30 minutes.
- Treat other HTTP responses and invalid signing material as terminal dead-letter outcomes.
- Receivers deduplicate retries using the stable event ID.

## Acceptance

- Public address families are accepted only when every DNS result is public unicast; private, loopback, link-local, shared, multicast, documentation, NAT64, and reserved ranges fail closed.
- Mixed public/private DNS answers are rejected before a socket is opened.
- Redirect responses are not followed; the response is treated as a terminal failure.
- Sensitive Outbox fields are absent from sent payloads.
- Duplicate fan-out, multi-worker claim races, retry scheduling, lease expiry, endpoint disablement, and append-only attempt history are covered.
- CI passes migration validation, API health checks, tests, and container image build.

## Environment gate

Stage and Production delivery remain disabled until the deployment owner provisions a separate 32-byte encryption key per environment, applies migration 056, and explicitly enables `Mono:WebhookDelivery:Enabled`.
