# Sprint 005 — Customer Branch Read API

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E16 — B2B API / E20 — Security
- Branch: `sprint-005-customer-branch-api`
- Base: `sprint-004-managed-api-keys`

## Sprint goal

Expose a first customer-scoped B2B API operation that Hana can use to read its Mono branch configuration.

## Scope

- Add `GET /v1/branches`.
- Derive customer scope only from the authenticated API key.
- Require the `branches.read` scope.
- Return a stable, customer-facing branch summary without internal customer identifiers.
- Verify deterministic ordering, tenant isolation, authentication, and scope authorization.

## Acceptance criteria

1. Requests without a valid API key receive an authentication challenge.
2. Authenticated keys without `branches.read` receive a forbidden response.
3. Branch listing always filters by the authenticated customer context.
4. The response contains only public branch summary fields and is ordered by name then ID.
5. Health endpoints remain independent of database-backed credentials.

## Out of scope

- Branch creation or update endpoints.
- Delivery quote/request and mission endpoints.
- Credential provisioning workflow.
- Staging, deployment, or production changes.
