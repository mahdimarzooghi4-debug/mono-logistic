# Sprint 072 — Replay Before Source Verification

## Goal

Honor an already committed Quote idempotency mapping before depending on a live distance source verifier, while preserving evidence-envelope validation and tenant boundaries.

## Scope

- Add a tenant-scoped, read-only lookup for a Quote issuance Idempotency-Key and canonical request hash.
- On an exact match, return the original Quote ID and version before expiry evaluation or another source-verifier call.
- On a different request or hash for that key, return an idempotency conflict before source verification.
- Keep evidence integrity validation ahead of replay lookup.
- Retain the transactional advisory-lock and request-row-lock path for first issuance and concurrent races.
- Extend service and HTTP/PostgreSQL integration tests for replay during verifier unavailability.

## Acceptance

- A valid exact retry replays even if its supplied expiry is no longer in the future.
- A committed retry does not call the source verifier again.
- Invalid evidence cannot use an existing idempotency record.
- Changed input with a used key conflicts.
- A key miss still requires a verified source and follows the atomic first-issue path.
- Tenant-scoped lookup returns no record from another customer's key space.

## Exclusions

No provider trust configuration, Quote renewal, Mission confirmation, Stage, deployment, or release work.
