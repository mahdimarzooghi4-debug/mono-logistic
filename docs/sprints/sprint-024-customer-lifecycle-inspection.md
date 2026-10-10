# Sprint 024 — Customer Lifecycle Inspection

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-024-customer-lifecycle-inspection`
- Base: `sprint-023-managed-api-key-inspection`

## Sprint goal

Allow Mono administrators to verify a customer's registry status and review the immutable onboarding and activation history through the operations API.

## Scope

- Add a read-only customer summary route scoped by customer ID.
- Add a read-only customer audit-history route returning action, actor, reason, and occurrence time.
- Require the existing Mono administrator authorization policy for both routes.
- Return 400 for malformed customer IDs and 404 for unknown customers.
- Preserve chronological audit ordering and do not mutate customer or audit records.
- Add integration coverage for authorization, lifecycle states, history, and not-found behavior.

## Acceptance criteria

1. Unauthenticated requests receive 401; non-administrators receive 403.
2. An administrator can inspect the customer's profile and current status.
3. Audit history includes creation and activation with actor and reason in chronological order.
4. Unknown customer IDs return 404.
5. Read routes do not change customer state or append audit entries.
6. CI build, tests, and container image build pass.

## Out of scope

Customer creation or activation changes, customer listing or search, API key issuance, Hana-specific policy, Stage, and deployment.
