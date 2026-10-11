# Sprint 065 — Versioned Quote Contract

- Status: In progress
- Phase: B — Logistics Core
- Epic: E8 — Pricing
- Capability: E8.3 — Versioned, expiring Quote contract
- Branch: sprint-065-versioned-quote-contract
- Base: sprint-064-hana-distance-pricing

## Scope

- Define a tenant-scoped Quote snapshot with version, issue time, explicit expiry, Hana fare, policy versions, and audit-evidence references.
- Validate the fare against the recorded distance and Hana tariff version.
- Evaluate expiry at a supplied instant without choosing a default lifetime.

## Acceptance criteria

1. Valid Quote snapshots require a positive version and expiry later than issue time.
2. A Quote’s pricing policy version must match its fare.
3. Tampered fare amounts are rejected when they no longer match the tariff and distance.
4. Audit-evidence references are required and unique.
5. A quote is expired at the exact expiry instant.
6. Tenant and delivery-request identifiers use Mono’s existing formats.

## Out of scope

Choosing TTL, issuing or persisting quotes, version sequencing, renewal, API endpoints, payment, Stage, and deployment.
