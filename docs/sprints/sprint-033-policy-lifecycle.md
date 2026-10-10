# Sprint 033 — Policy Lifecycle

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E2 — Policy Engine
- Branch: `sprint-033-policy-lifecycle`
- Base: `sprint-032-policy-draft-registry`

## Sprint goal

Allow Mono administrators to activate and retire policy versions with a complete, append-only audit trail.

## Scope

- Activate a Draft version; transition any existing active version under the same policy to Inactive atomically.
- Retire Active or Inactive versions without deleting version definitions.
- Require MFA-gated Mono administrator access and a reason for every transition.
- Enforce at most one Active version per policy in PostgreSQL.
- Add integration coverage and update the policy authoring API documentation.

## Acceptance criteria

1. Only Draft versions can be activated; only Active or Inactive versions can be retired.
2. An expired version cannot be activated.
3. Concurrent lifecycle operations serialize on the policy identity, and the database prevents multiple Active versions.
4. Every changed status and its audit snapshot commit in the same transaction.
5. Unauthenticated and non-administrator callers cannot transition policy versions.
6. No policy resolution, evaluation, order decision, or financial posting is added.

## Out of scope

Effective policy resolution, family-specific definition validation, runtime decision evaluation, fund or ledger posting, Stage, and deployment.
