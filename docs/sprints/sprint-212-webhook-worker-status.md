# Sprint 212 — Webhook delivery worker status

## Goal
Make the sender process state visible to Operations alongside the existing webhook delivery queue summary.

## Delivered
- Track whether the sender is disabled, blocked by missing secret protection, starting, healthy, or degraded after a failed batch.
- Add an administrator-only, no-store worker status endpoint.
- Report only process-local enablement, timestamps, and last claimed batch size; do not expose secrets or exception details.
- Recover to healthy after the next successful batch without affecting API readiness.
- Correct the runbook's replay guidance and document per-instance status limitations.
- Add status transition tests and backlog traceability.

## Verification
- GitHub Actions runs build, migration validation, API smoke tests, tests, and container validation for this draft.
