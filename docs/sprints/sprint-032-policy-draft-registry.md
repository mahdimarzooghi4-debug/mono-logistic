# Sprint 032 — Policy Draft Registry

- Status: In progress
- Phase: A — Platform Foundation
- Epic: E2 — Policy Engine
- Branch: `sprint-032-policy-draft-registry`
- Base: `sprint-031-branch-external-identifiers`

## Sprint goal

Create a versioned, auditable authoring boundary for policy drafts before implementing policy activation, resolution, or evaluation.

## Scope

- Create a policy identity and first Draft version using the accepted policy schema.
- Create additional immutable Draft versions under the same policy identity.
- List policies with keyset pagination and inspect one policy's versions.
- Persist immutable audit records for creation and each new version.
- Require MFA-gated Mono administrator access.
- Document the internal API and add PostgreSQL-backed integration coverage.

## Acceptance criteria

1. Unauthenticated or non-administrator requests cannot list or author policies.
2. A valid request creates version 1 in `DRAFT` state with the actor and reason recorded.
3. Additional Draft versions increment monotonically and preserve previous definitions.
4. Scope, enforcement type, JSON-object definition, and effective-date ordering are validated.
5. Audit entries are append-only and contain a snapshot of the authored version.
6. Policy list pagination is stable and malformed cursors fail closed.
7. No route activates, retires, resolves, evaluates, or applies a policy.

## Out of scope

Policy activation/retirement, precedence resolution, family-specific definition validation, runtime decision evaluation, financial posting, Stage, and deployment.
