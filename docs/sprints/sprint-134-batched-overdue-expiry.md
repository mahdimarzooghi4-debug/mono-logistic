# Sprint 134 — Batch overdue offer expiry

## Goal

Let operations resolve several overdue offers with one bounded request while keeping each expiry independently auditable and retryable.

## Delivered

- Add an administrator-only batch endpoint accepting up to 20 offer-expiry commands.
- Validate all IDs, idempotency keys, and duplicate offers before executing any command.
- Execute each expiry through the existing idempotent transition and return a per-item outcome; partial success is explicit.
- Cover duplicate rejection, successful expiry, missing offers, and replayed commands in integration tests.
- Document the request and retry semantics.
