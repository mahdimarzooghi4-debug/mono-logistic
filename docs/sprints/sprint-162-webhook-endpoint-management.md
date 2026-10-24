# Sprint 162 — Webhook endpoint management

## Goal

Let an API customer inspect and safely manage its registered webhook endpoints without exposing stored signing material.

## Scope

- Add tenant-scoped, keyset-paginated endpoint list and metadata detail reads.
- Add endpoint disablement. Repeated disable requests return the same disabled state.
- Add signing-secret rotation for active endpoints; reveal the new secret only in the successful rotation response.
- Reject rotation for disabled endpoints and fail closed when the encryption key is not configured.
- Record creation, disablement, and secret rotation in an append-only lifecycle event table.

## Security boundaries

- All routes require the authenticated customer tenant and `webhooks.manage` scope.
- List/detail responses never expose the encrypted secret or plaintext secret.
- Endpoint IDs are always queried with the authenticated tenant ID; other tenants' IDs return `404`.
- Lifecycle mutation and history insert commit in one database transaction.
- No outbound callback requests are made in this sprint.

## Acceptance

- A tenant can page through its own endpoints and retrieve metadata for its own endpoint.
- A foreign or unknown endpoint ID is indistinguishable and returns `404`.
- Disable is repeatable, rotation returns a new one-time secret, and disabled rotation returns `409`.
- Lifecycle audit rows cannot be updated, deleted, or truncated.
- Build, migration validation, API health checks, tests, and container image build pass in CI.
