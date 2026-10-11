# Sprint 064 — Hana Distance Pricing v1

- Status: In progress
- Phase: B — Logistics Core
- Epic: E8 — Pricing
- Capability: E8.1/E8.2 — Hana distance tariff calculation
- Branch: sprint-064-hana-distance-pricing
- Base: sprint-063-feasibility-source-verification-boundary

## Scope

- Implement the approved five-band Hana Phase-1 distance tariff as a pure deterministic calculator.
- Return tariff version, distance band, total fare, and the approved 50/50 buyer/seller split.
- Reject distances outside the documented 0–5 km range.

## Acceptance criteria

1. Each tariff band returns the fare recorded in the business decision.
2. Exact kilometer boundaries use the next band; exactly 5 km remains in the last band.
3. Each share is half of total fare and shares sum to total.
4. Negative distances and distances above 5 km are rejected.
5. Fare remains independent of courier mode.

## Out of scope

Quote expiry or persistence, price modifiers, distance-provider integration, courier payout, commission, VAT, settlement, Stage, and deployment.
