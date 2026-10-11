# Sprint 068 — Hana Distance Pricing Evidence

- Status: In progress
- Phase: B — Logistics Core
- Epic: E8 — Pricing
- Capability: E8.3 — Verify evidence used for distance pricing
- Branch: sprint-068-quote-distance-evidence
- Base: sprint-067-quote-read-api

## Scope

- Add a canonical SHA-256 integrity envelope that binds measured metres, evidence ID, observation time, source ID, and opaque source attestation.
- Add a customer-bound source-verifier interface with a safe unconfigured implementation.
- Register the unconfigured verifier in dependency injection.
- Document trust limits and add domain tests.

## Acceptance criteria

1. Any change to distance or source metadata invalidates the integrity digest.
2. Equivalent UTC offsets and decimal scales produce the same digest.
3. Distance is limited to the approved Hana tariff range of 0–5,000 metres.
4. The digest is explicitly not treated as proof of source authenticity.
5. The default verifier returns `DISTANCE_SOURCE_VERIFIER_NOT_CONFIGURED`.
6. No provider, cryptographic attestation method, expiry/TTL, or Quote issuance behavior is invented.

## Out of scope

Provider integration and key management, Quote issuance endpoint/persistence of source evidence, TTL policy, renewal, payment, Mission creation, Stage, and deployment.
