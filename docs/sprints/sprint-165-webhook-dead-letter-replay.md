# Sprint 165 — Webhook dead-letter replay

## Objective

Allow customer integrations to request a safe, auditable retry of an unrecoverable webhook delivery.

## Scope

- Add `POST /v1/webhook-deliveries/{delivery_id}/retry`, protected by `webhooks.manage` and a required `Idempotency-Key`.
- Permit replay only for `DEAD_LETTERED` deliveries whose endpoint is still active.
- Create a distinct delivery ID and replay generation; preserve the original delivery and attempt timeline.
- Make repeated use of the same key return the same replay generation and cap manual replay at three generations.
- Store replay request idempotency records as append-only rows.

## Acceptance

- Foreign or unknown IDs return the same 404; invalid keys return 400.
- Non-dead-lettered, disabled endpoint, and exhausted replay limit requests return 409.
- Concurrent requests using one key create one generation; distinct keys cannot exceed the cap.
- The replay enters the existing Outbox delivery queue and follows the same signature, SSRF, timeout, and bounded automatic retry rules.
- CI build, migration validation, API health checks, tests, and container image build pass.
