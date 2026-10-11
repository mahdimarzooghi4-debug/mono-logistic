# Sprint 063 — Feasibility Evidence Source Verification Boundary

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1/E7.3 — Source verification before Promise persistence
- Branch: sprint-063-feasibility-source-verification-boundary
- Base: sprint-062-feasibility-evidence-integrity

## Scope

- Add source ID and opaque attestation fields to criterion evidence integrity envelopes.
- Include both fields in the existing canonical digest.
- Add a customer-bound verifier contract and fail-closed default.
- Require every criterion in the requested and alternative evidence sets to verify before persistence.
- Register the application service and safe default in dependency injection.

## Acceptance criteria

1. Changing the source ID or attestation invalidates the envelope digest.
2. A verifier receives the authenticated customer ID and criterion for every required result.
3. Any unverified or unconfigured source prevents persistence.
4. A test verifier can allow a known test source, without selecting a production trust scheme.
5. Documentation makes clear that source registration and verification method remain undecided.

## Out of scope

Production source adapters, signature or key-management selection, freshness policy, evidence endpoints, route simulation, ETA or safety-buffer calculation, Stage, and deployment.
