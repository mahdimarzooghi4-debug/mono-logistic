# Quote Persistence v1

## Stored snapshot

Sprint 066 persists validated DeliveryQuote snapshots in PostgreSQL, scoped to the authenticated customer and delivery request. Each row stores an immutable JSON snapshot, a SHA-256 hash of its canonical representation, the explicit issue and expiry timestamps, and the quote version.

The relational key permits one version per customer and delivery request. The request row is locked during writes so concurrent requests cannot create gaps or duplicate versions.

## Write behavior

- Version 1 starts a quote history; later writes must use the next consecutive version.
- Repeating the same version with the same quote ID and canonical snapshot returns Replayed.
- Reusing that version with a different ID or snapshot returns Conflict.
- A quote belonging to another tenant is rejected before database access.
- A request not visible to the authenticated tenant returns DeliveryRequestNotFound.
- Each write validates the complete quote contract before persistence.

The caller provides ExpiresAt. Persistence does not set a default TTL or extend expiration during replay.

## Scope limits

This sprint adds storage and version-safe writes only. Quote calculation is available as the Hana Phase-1 tariff contract, but quote issuance, expiry policy, read API, confirmation, payment, Mission creation, outbox publication, Stage, and deployment remain separate work. Audit evidence IDs are stored as references and are not independently verified by the Quote repository.
