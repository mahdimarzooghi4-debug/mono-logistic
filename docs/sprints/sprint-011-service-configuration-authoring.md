# Sprint 011 — Internal Service Configuration Authoring

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-011-service-configuration-authoring`
- Base: `sprint-010-customer-service-configuration`

## Sprint goal

Create a safe internal foundation for authoring versioned customer/service configuration without exposing configuration controls to B2B clients.

## Scope

- Create configuration versions in Draft state with monotonically increasing per-customer/service versions.
- Require a trusted Mono operations context with the `MONO_ADMIN` role.
- Activate a Draft only when its effective period does not overlap another active version.
- Record actor, action, reason, configuration identity, and time in an append-only audit table.
- Keep authoring and activation out of public HTTP routes until Mono staff authentication and authorization are implemented.

## Acceptance criteria

1. Missing or non-admin operations identity fails closed.
2. Configuration documents must be JSON objects and effective end must be after effective start.
3. Concurrent Draft creation for one customer/service receives unique sequential versions.
4. Activation serializes per customer/service and rejects overlapping effective intervals.
5. Audit entries are written in the same transaction as the configuration change and cannot be updated or deleted.
6. Tests cover role checks, versioning, successful activation, overlap rejection, and audit.
7. CI builds, runs PostgreSQL tests, and builds the API container.

## Out of scope

- Mono staff login, SSO/MFA, endpoint or admin UI wiring.
- Hana-specific configuration values or seed data.
- Deactivation/retirement, approval workflow, pricing, feasibility, Stage, server deployment, or production changes.
