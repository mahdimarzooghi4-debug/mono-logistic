# Sprint 062 — Feasibility Evidence Integrity

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.1/E7.3 — Evidence integrity before Promise persistence
- Branch: sprint-062-feasibility-evidence-integrity
- Base: sprint-061-promise-version-history-api

## Scope

- Add a canonical SHA-256 integrity envelope to criterion result records.
- Validate integrity for every required E7.1 criterion before Promise persistence.
- For an alternative Promise, validate both requested-time and alternative-time criterion result sets.
- Block persistence for missing, malformed, duplicate, or modified envelopes.

## Acceptance criteria

1. The digest is stable across reason ordering and equivalent UTC offsets.
2. Mutating result fields or integrity metadata invalidates the digest.
3. Exactly one valid envelope is required for each E7.1 criterion at commit time.
4. ACCEPTED and ALTERNATIVE_PROMISE paths make no persistence call when integrity is missing or invalid.
5. Existing read-only feasibility evaluation remains usable without an integrity envelope.
6. Documentation states clearly that a digest detects payload mutation but does not prove source identity or assertion truth.

## Out of scope

Trusted source identity, signatures, key management, freshness policy, external integrations, route simulation, ETA or safety-buffer calculation, Stage, and deployment.
