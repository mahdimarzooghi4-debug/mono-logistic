# Sprint 057 — Promise Commit Contract

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.3 — Promise
- Branch: sprint-057-promise-commit-contract
- Base: sprint-056-feasibility-outcome-evaluation

## Scope

- Define a typed Promise commit proposal.
- Validate decision ID, positive Promise version, timestamps, non-rejected outcome, non-negative safety buffer, buffer policy version, policy-version evidence, and reason codes.
- Require committed delivery time to be later than evaluation time.
- Keep validation separate from persistence and operational commitment.

## Acceptance criteria

1. ACCEPTED and ALTERNATIVE_PROMISE proposals with complete metadata validate.
2. REJECTED proposals, invalid versions/timestamps, negative buffers, missing policy versions, and malformed/empty reasons fail validation.
3. Validation does not calculate a buffer, verify upstream feasibility, persist data, or create a Mission.

## Out of scope

Safety-buffer formula or policy, version sequencing against stored records, authorization, database migrations, atomic persistence/outbox, APIs, Mission creation, Stage, and deployment.
