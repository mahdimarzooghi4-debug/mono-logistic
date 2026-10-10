# Sprint 037 — Shipment Facts

- Status: In progress
- Phase: B — Logistics Core
- Epic: E4 — Shipment & Capability Model
- Branch: `sprint-037-shipment-facts`
- Base: `sprint-036-optimization-policy-inputs`

## Sprint goal

Complete the first shipment-fact fields on the existing delivery-request contract without creating Mission or feasibility behavior.

## Scope

- Add optional `package_count`, `estimated_volume_m3`, and `temperature_class` fields to shipment input.
- Normalize temperature class to uppercase and accept the catalog values `AMBIENT`, `CHILLED`, and `FROZEN`.
- Validate optional package count and volume as positive when supplied.
- Preserve old canonical request payloads and idempotency matching when new fields are omitted.
- Add API contract, backlog, and validation-test updates.

## Acceptance criteria

1. Existing shipment requests remain valid without the added optional facts.
2. Supplied package count and cubic-metre volume must be positive.
3. Supplied temperature class must match the policy catalog.
4. Omitted new fields remain absent from canonical JSON, preserving retries across this contract extension.
5. Shipment facts remain descriptive input; no feasibility, Mission, pricing, or dispatch decision is added.

## Out of scope

Feasibility, candidate eligibility, temperature capability matching, package/bag equivalence rules, maps, Stage, and deployment.
