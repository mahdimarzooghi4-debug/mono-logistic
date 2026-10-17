# Sprint 210 — Courier offer expiry worker status

## Goal
Give Operations a safe way to tell whether the optional automatic offer expiry worker is disabled, running successfully, or failing.

## Delivered
- Track process-local worker state, latest attempt/success timestamps, and last successful batch size.
- Add an administrator-only, no-store status endpoint.
- Report disabled workers as disabled, without treating the API as unhealthy.
- Mark the worker degraded after a failed batch and healthy again after a successful batch.
- Keep exception details and courier/Mission data out of the status response.
- Add focused state transition tests and document multi-instance limitations.

## Verification
- GitHub Actions runs build, tests, migration checks, smoke tests, and container validation for this draft.
