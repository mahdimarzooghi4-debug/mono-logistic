# Sprint 046 — Zone Capacity Snapshot

- Status: In progress
- Phase: B — Logistics Core
- Epic: E6 — Zone & Capacity
- Capability: E6.3 — Capacity
- Branch: sprint-046-zone-capacity
- Base: sprint-045-courier-zone-approval

## Sprint goal

Add a per-zone snapshot contract for registered, online, and effective capacity without inventing calculation rules or Hana's target formula.

## Scope

- Represent a zone ID, observation timestamp, and separate registered, online, and effective counts.
- Require the zone to exist in a supplied valid zone registry.
- Require a non-default observation timestamp and non-negative counts.
- Keep count derivation, freshness, capacity ordering, policy, and health outside the contract.
- Correct the E6 backlog placement/duplicate text found during implementation.

## Acceptance criteria

1. A valid snapshot for a known zone passes.
2. Zero counts are valid.
3. Missing or unknown zone IDs and a missing observation timestamp are rejected.
4. Negative values for any of the three counts are rejected.
5. The validator does not calculate or constrain relationships among the counts.
6. No persistence, Hana capacity formula, allocation, health, promise, courier assignment, operations API, Stage, or deployment is added.

## Out of scope

Database storage, aggregation from courier records, staleness thresholds, active_hana_stores × 10, zone health, allocation, assignment, API endpoints, Stage, and deployment.
