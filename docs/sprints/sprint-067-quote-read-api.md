# Sprint 067 — Tenant-scoped Quote Read API

- Status: In progress
- Phase: B — Logistics Core
- Epic: E8 — Pricing
- Capability: E8.3 — Read Quote snapshot and version history
- Branch: sprint-067-quote-read-api
- Base: sprint-066-quote-persistence

## Scope

- Add authenticated current Quote and version-history read endpoints.
- Reuse delivery-requests.read and scope queries to the authenticated customer.
- Return customer-visible pricing and expiration fields only.
- Mark expiry at read time without extending or renewing it.

## Acceptance criteria

1. The current endpoint returns the highest persisted version for the tenant-owned request.
2. The history endpoint is newest-first and paginated with page_size and before_version.
3. Missing and cross-tenant Quotes return the same QUOTE_NOT_FOUND response.
4. Responses exclude audit-evidence IDs and internal policy-version evidence.
5. is_expired is true at or after the explicitly stored expiry instant; reads do not change persistence.
6. Missing authentication, insufficient scope, invalid pagination, and not-found cases have defined HTTP results.
7. PostgreSQL integration tests cover owner, other tenant, empty request, expiry, pagination, response fields, and access control.

## Out of scope

Quote issuance, trust/validation of the distance source, TTL selection or renewal, payment, Mission creation, Stage, and deployment.
