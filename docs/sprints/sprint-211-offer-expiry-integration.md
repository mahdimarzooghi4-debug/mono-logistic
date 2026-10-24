# Sprint 211 — Automatic offer expiry integration coverage

## Goal
Verify the opt-in offer expiry worker against PostgreSQL and the real offer lifecycle service before the staged test cycle.

## Delivered
- Extend the existing PostgreSQL Mission lifecycle integration test to exercise the worker dispatcher.
- Prove an unexpired offer is left untouched.
- Advance the test clock, then verify the overdue offer is expired by the worker path.
- Verify the immutable event records actor mono-dispatch-expiry-worker and the existing expiry reason.
- Verify a subsequent batch does not select an already-expired offer.

## Verification
- GitHub Actions runs the database integration suite, migration validation, API smoke tests, and container build for this draft.
