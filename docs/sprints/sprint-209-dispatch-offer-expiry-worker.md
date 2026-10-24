# Sprint 209 — Automatic courier offer expiry

## Goal
Close expired courier offers automatically when enabled, while preserving the existing operator-controlled dispatch policy.

## Delivered
- Add a background worker, disabled by default, with bounded polling and batch settings.
- Select only offers whose stored expiry has passed and whose Mission remains in SEARCHING_COURIER.
- Reuse the existing transactional, idempotent expiry service and append-only offer event history.
- Attribute worker events to the explicit system actor mono-dispatch-expiry-worker.
- Do not generate replacement offers or introduce courier ranking/selection behavior.
- Document configuration and add default-off and batch-bound tests.

## Verification
- GitHub Actions runs build, tests, migration checks, and container validation for this draft.
