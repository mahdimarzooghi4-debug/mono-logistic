# Sprint 066 — Quote Persistence

- Status: In progress
- Phase: B — Logistics Core
- Epic: E8 — Pricing
- Capability: E8.3 — Persist versioned, expiring Quote snapshots
- Branch: sprint-066-quote-persistence
- Base: sprint-065-versioned-quote-contract

## Scope

- Persist validated Quote snapshots in PostgreSQL, scoped to the authenticated tenant and delivery request.
- Preserve immutable version history and caller-supplied issue/expiry instants.
- Require consecutive versions and make identical same-version writes safely replayable.
- Reject cross-tenant Quote snapshots and hide delivery requests outside the tenant.

## Acceptance criteria

1. Every stored Quote passes DeliveryQuoteContract.Validate.
2. A Quote's customer ID must match the authenticated customer context.
3. A request must belong to that tenant before the write is accepted.
4. First version is 1; subsequent versions are consecutive and concurrent writers serialize on the request row.
5. Identical same-version writes return Replayed; changed content or a skipped version returns Conflict.
6. The database stores a canonical JSON snapshot and content hash for audit comparison.
7. Expiry is taken from the Quote; storage introduces no default TTL.

## Out of scope

Quote calculation or issuance endpoint, read API, TTL selection, renewal, confirmation/payment, Mission creation, evidence source verification, outbox publication, Stage, and deployment.
