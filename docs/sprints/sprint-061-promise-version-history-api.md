# Sprint 061 — Promise Version History API

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.3 — Promise version history
- Branch: sprint-061-promise-version-history-api
- Base: sprint-060-promise-read-api

## Scope

- Add a paginated endpoint for customer-visible Promise version history.
- Reuse delivery-requests.read and tenant-scoped persistence.
- Return version, committed delivery time, outcome, and record timestamp only.
- Cover pagination, cross-tenant privacy, missing Promise, and invalid cursors.

## Acceptance criteria

1. A tenant can page through its Promise history in descending version order.
2. The next cursor retrieves older versions without duplicates.
3. A foreign tenant receives the same not-found response as an absent Promise.
4. A missing Promise returns 404 and invalid pagination returns 400.
5. Internal policy, buffer, decision, hash, outbox, and raw proposal values are not returned.
6. The endpoint remains read-only and uses delivery-requests.read.

## Out of scope

Promise writes, Mission lifecycle, evidence provenance, outbox delivery, Stage, and deployment.
