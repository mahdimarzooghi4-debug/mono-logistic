# Sprint 018 — Mono Operations Customer Onboarding

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-018-operations-customer-onboarding`
- Base: `sprint-017-operations-configuration-retirement`

## Sprint goal

Enable Mono administrators to register a customer in a controlled pending state and activate it only after onboarding approval, without customer-specific code or seeded customer records.

## Scope

- Add administrator-only operations routes to create and activate customers.
- Generate opaque Mono customer IDs; never accept customer IDs from create request data.
- Create customers in `PENDING`; require an explicit activation action to reach `ACTIVE`.
- Require an operational reason and append actor/action/time audit records transactionally.
- Keep credential provisioning separate from customer creation and activation.
- Add PostgreSQL-backed HTTP integration coverage and operations documentation.

## Acceptance criteria

1. Missing/invalid tokens receive 401 and non-administrator identities receive 403.
2. Creating a customer returns a Mono-generated ID and `PENDING` status.
3. Activation changes only a pending customer to `ACTIVE`; repeat or invalid transitions return 409.
4. Unknown customer IDs return 404 without disclosing customer data.
5. Customer state changes and append-only audit records commit atomically.
6. No Hana-specific record, API credential, or configuration is created.
7. CI build, tests, and container image build pass.

## Out of scope

Branch onboarding, customer credential issuance, Hana seed data, operations UI, Stage, and deployment.
