# Sprint 015 — Operations Configuration Inspection API

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-015-operations-configuration-inspection-api`
- Base: `sprint-014-operations-jwt-verification-tests`

## Sprint goal

Expose the existing read-only service configuration inspection capability over a dedicated, Mono-administrator-only operations API.

## Scope

- Add read-only routes for version metadata and append-only audit history.
- Require the dedicated `MonoOperationsJwt` administrator authorization policy on both routes.
- Keep customer API-key credentials and customer-facing routes outside this authorization path.
- Bound each result to a customer, service code, and a 1–100 row limit (default 50).
- Return invalid customer/service/limit input as a generic 400 response.
- Do not return the configuration JSON payload or add authoring/activation routes.

## Acceptance criteria

1. Requests without a valid operations token receive 401; authenticated non-administrators receive 403.
2. A valid Mono administrator can read configuration version summaries and audit history for the requested customer/service.
3. The version response contains metadata only and never returns the stored configuration JSON.
4. Invalid identifiers and limits outside 1–100 return 400 without exposing exception details.
5. PostgreSQL-backed integration tests cover route authorization, successful reads, audit visibility, payload exclusion, and invalid limits.
6. CI build, tests, and container image build pass.

## Out of scope

Configuration creation or activation endpoints, operations UI, identity-provider selection, Hana-specific records, Stage, and deployment.
