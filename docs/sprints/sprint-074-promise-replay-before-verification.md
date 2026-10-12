# Sprint 074 — Promise replay before source verification

## Goal

Make a successfully committed Promise request safely replayable when its source verifier is temporarily unavailable.

## Contract

- Fingerprint the complete canonical HTTP request, including delivery request ID, Idempotency-Key/decision ID, requested criteria and evidence envelopes, optional alternative-search evidence, and the proposal.
- Evaluate feasibility and validate evidence-integrity envelopes before consulting the idempotency mapping.
- Look up the tenant-scoped decision mapping before source verification. Replay only when the request fingerprint and delivery request match; return a conflict for a reused decision ID with changed input.
- Persist the request fingerprint in the same atomic Promise version/outbox transaction so concurrent first submissions remain safe.
- Keep the existing service and repository commit behavior available for non-HTTP callers.
- Keep production source verification fail-closed for new decisions. A replay does not call or trust the verifier again.

## Verification

The PostgreSQL-backed HTTP test commits with a test verifier, marks that verifier unavailable, and confirms an exact retry still returns the Promise without another verifier call. It also confirms tampered evidence is rejected before replay lookup, changed valid input conflicts, and only one Promise version is persisted.

No new feasibility rule, external provider, Mission creation, Stage, or deployment is introduced.
