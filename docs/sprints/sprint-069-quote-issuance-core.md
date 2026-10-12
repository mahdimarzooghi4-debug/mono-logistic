# Sprint 069 — Quote Issuance Core

- Status: In progress
- Phase: B — Logistics Core
- Epic: E8 — Pricing
- Capability: E8.3 — Issue first immutable Quote version
- Branch: sprint-069-quote-issuance-core
- Base: sprint-068-quote-distance-evidence

## Scope

- Add an application service for first-version Quote issuance.
- Require explicit future expiry and valid pricing evidence.
- Verify evidence for the authenticated customer before persistence.
- Calculate fare server-side from the Hana tariff.
- Persist an internal evidence reference without returning it to customers.
- Add tests for calculation, tenant binding, fail-closed verification, invalid evidence, and expiry.

## Acceptance criteria

1. Only a verified source evidence result reaches Quote persistence.
2. The Quote fare is calculated from measured metres; caller-supplied fares do not exist.
3. Quote expiry is caller-supplied and must be after server issue time.
4. The Quote uses the authenticated customer and requested delivery request IDs.
5. The first Quote is version 1 and carries the matching pricing policy version and evidence ID.
6. Default unconfigured verification blocks persistence.
7. Existing repository tenant and version locking remains the persistence authority.

## Out of scope

HTTP endpoint and idempotency behavior, renewal, trusted provider/verifier implementation, TTL policy, payment, Mission creation, Stage, and deployment.
