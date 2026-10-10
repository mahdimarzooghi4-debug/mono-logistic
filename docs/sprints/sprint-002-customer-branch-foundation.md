# Sprint 002 — Customer & Branch Foundation

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration
- Branch: `sprint-002-customer-branch-foundation`
- Base: `sprint-001-platform-foundation`

## Sprint goal

Add the first shared customer and branch persistence slice for Mono, so Hana can be represented as a customer configuration without customer-specific code.

## Scope

- Add Customer and Branch domain records with opaque prefixed identifiers.
- Persist the documented Customer and Branch fields in PostgreSQL.
- Require every Branch to reference a Customer.
- Scope branch lookup by both customer and branch IDs.
- Verify persistence and tenant scoping against PostgreSQL in CI.

## Acceptance criteria

1. Customer and Branch IDs use the documented opaque prefixes and are not sequential.
2. Branch rows require an existing customer.
3. Branch lookup always includes the customer ID.
4. Latitude and longitude are either both absent or both within valid coordinate ranges.
5. Optional fields remain nullable where the domain documents do not define requiredness.
6. Database integration coverage passes against PostgreSQL 18 in CI.
7. No Hana customer or branch data is seeded.
8. No public write endpoint is added before authentication and authorization are implemented.

## Out of scope

- Authentication, customer context resolution, public/customer APIs, branch update/archive workflows, contracts, service configurations, policies, and staging/deployment.
