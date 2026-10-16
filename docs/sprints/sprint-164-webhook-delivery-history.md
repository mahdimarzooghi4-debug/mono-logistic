# Sprint 164 — Webhook delivery history

## Objective

Give customer integrations a safe, tenant-scoped way to inspect webhook delivery outcomes and immutable attempt timelines.

## Scope

- Add `GET /v1/webhook-deliveries` with endpoint, event type, and status filters plus bounded keyset pagination.
- Bind cursors to the exact filter set so they cannot be reused against a different query.
- Add `GET /v1/webhook-deliveries/{delivery_id}` with immutable attempt events.
- Require `webhooks.manage` and enforce customer ownership in every repository query.
- Omit destination URLs, payload contents, and signing-secret material from delivery-history responses.

## Acceptance

- A customer sees only its own delivery records; unknown and foreign delivery IDs return the same 404.
- Invalid filters, limits, and filter-mismatched cursors return 400.
- Attempt events are returned in append order and remain immutable.
- Existing CI build, migration validation, API health checks, tests, and container build pass.

## Deferred

Manual replay is deferred until the delivery identity, attempt numbering, duplicate handling, and operator authorization rules are specified.
