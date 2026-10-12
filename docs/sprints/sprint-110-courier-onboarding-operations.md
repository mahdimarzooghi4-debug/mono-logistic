# Sprint 110 — Durable courier onboarding operations

## Goal

Turn the Sprint 109 trust state machine into a usable Mono operations workflow with durable courier records and an auditable transition history.

## Scope

- Add PostgreSQL persistence for courier mode, preferred/approved zones, lifecycle status, and creation/update attribution.
- Add MFA-gated operations routes to register a courier, list the roster with keyset pagination, inspect courier detail/history, and advance trust status one step at a time.
- Require idempotency keys for registration and trust transitions; persist replay responses atomically with the state/history writes.
- Require preferred and approved zones to reference currently active Mono zone geometry. Approved zones must be chosen from preferences; a primary zone must be approved.
- Enforce lifecycle order and status/history consistency in both the domain and PostgreSQL.
- Keep trust transitions and idempotency records append-only.
- Add isolated-schema PostgreSQL integration coverage for create/replay/conflict, lifecycle completion, invalid progression/zone approval, pagination/detail, and database mutation guards.

## Data boundary

The registry stores only a Mono Courier ID, mode, operational zones, lifecycle status, staff attribution, reason codes, and opaque evidence references. It does not accept names, phone numbers, national IDs, bank information, profile photos, document contents, or courier credentials. A reference does not prove that underlying evidence was verified.

## Operational boundary

This Sprint does not authenticate couriers, collect or verify their documents, record availability/location, or make ACTIVE couriers eligible for dispatch. OSM/GPS integration remains deferred. No Stage or deployment work is included.

## Acceptance criteria

- Registration requires MFA-gated Mono administrator authorization, an idempotency key, canonical mode, and active preferred zones.
- Registration creates a generated crr_ ID in REGISTERED status.
- Exact retries replay the original snapshot; changed requests using the same actor/action/key return an idempotency conflict.
- Trust status can advance only to the next documented status.
- ACTIVE requires an approved primary zone from the preferred set and currently active zone geometry.
- Courier state, immutable transition record, and idempotency snapshot commit atomically.
- Database triggers reject skipped transitions, status changes without matching history, and mutation/deletion/truncation of transition/idempotency ledgers.
- Roster pagination is keyset-based and bounded to 100 items; detail returns the ordered transition history.

## Validation

- PostgreSQL integration tests use per-test isolated schemas and the embedded Sprint 110 migration.
- Full CI runs .NET build/tests and container image build.
