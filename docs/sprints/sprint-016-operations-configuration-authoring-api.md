# Sprint 016 — Operations Configuration Authoring API

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E1 — Customer, Contract & Configuration / E20 — Security
- Branch: `sprint-016-operations-configuration-authoring-api`
- Base: `sprint-015-operations-configuration-inspection-api`

## Sprint goal

Expose the existing Draft creation and audited activation application service through the dedicated Mono administrator operations boundary.

## Scope

- Add an admin-only route to create a Draft for an explicit customer and service.
- Add an admin-only route to activate a Draft for that customer/service.
- Preserve existing input validation, effective-window overlap protection, and append-only audit recording in the application service.
- Return Draft metadata only; never echo the stored configuration JSON in the create response.
- Map invalid input to 400, unknown customer-scoped configuration to 404, and non-activatable/overlapping drafts to 409.
- Keep the routes separate from customer API-key authentication and public B2B APIs.

## Acceptance criteria

1. Missing/invalid tokens receive 401 and authenticated non-administrators receive 403 on both write routes.
2. An administrator can create a Draft and receives its ID/version/status without the stored payload.
3. An administrator can activate a Draft; activation produces an audit record and cannot overlap an active effective window.
4. A configuration ID belonging to another customer is not activated.
5. Invalid inputs, missing drafts, and activation conflicts map to 400, 404, and 409 respectively without leaking exception details.
6. CI build, tests, and container image build pass.

## Out of scope

Customer self-service configuration, policy-specific payload schemas not yet defined, Hana seed data, operations UI, Stage, and deployment.
