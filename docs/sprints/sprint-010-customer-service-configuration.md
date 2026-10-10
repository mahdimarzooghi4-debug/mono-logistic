# Sprint 010 — Versioned Customer Service Configuration

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E2 — Policy Engine
- Branch: `sprint-010-customer-service-configuration`
- Base: `sprint-009-delivery-request-list`

## Sprint goal

Add a tenant-scoped foundation for resolving the active, effective configuration version for a customer and Mono service.

## Scope

- Persist customer/service configuration versions with lifecycle status and effective dates.
- Store configuration documents as JSON objects without exposing them through a public endpoint.
- Resolve only records belonging to the authenticated customer context.
- Return no configuration when none is active and effective.
- Fail closed when multiple active versions overlap at the requested evaluation time.
- Register the repository for later use by the request/feasibility application layer.

## Acceptance criteria

1. Database constraints enforce configuration ID, service code, version, status, JSON object, and effective date validity.
2. Resolution uses tenant context, service code, status, and half-open effective interval.
3. A draft, expired, future-dated, or foreign-customer configuration is never returned.
4. Two overlapping active versions produce an explicit configuration conflict.
5. CI builds, runs PostgreSQL integration tests, and builds the API container.

## Out of scope

- Configuration authoring, approval, activation workflows, and admin UI.
- Hana-specific seed data.
- Pricing, feasibility, promise, courier, Mission, maps-provider selection, and AI behavior.
- Public configuration endpoints, Stage, server deployment, and production changes.
