# Sprint 109 — Courier trust lifecycle core

## Goal

Define and test Mono's ordered courier trust progression so future onboarding and assignment APIs cannot skip review stages or activate a courier without approved operating-zone data.

## Scope

- Add the ordered REGISTERED -> IDENTITY_VERIFIED -> DOCUMENTS_SUBMITTED -> TRUST_REQUIREMENTS_VERIFIED -> MONO_APPROVED -> ACTIVE lifecycle to the domain.
- Require every transition to carry a staff actor, canonical reason code, bounded opaque evidence reference, and UTC occurrence time.
- Reject skipped/repeated transitions and malformed identity, actor, reason, or evidence values.
- Require at least one approved zone and a primary zone from that approved set before activation.
- Add unit coverage for the full progression and fail-closed boundaries.

## Operational boundary

This is the lifecycle rule and its unit-tested domain core only. It does not collect personal documents, verify identity, store or inspect evidence, authenticate couriers, persist records, expose operations endpoints, or make a courier dispatch-eligible. Evidence references are pointers to separately reviewed material; this code does not attest to their authenticity. Activation is not connected to assignment.

Zone existence and geometry validation remain the responsibility of the existing zone registry and approval workflow.

## Acceptance criteria

- Only the next documented lifecycle status can be applied.
- Each successful transition returns an immutable attribution envelope with actor, reason, evidence reference, and normalized timestamp.
- Evidence references are bounded ASCII opaque identifiers and reject whitespace/control characters.
- ACTIVE requires a non-empty approved zone list and primary zone contained in that list.
- Existing modes remain extensible canonical identifiers; the core does not hard-code a fleet list.

## Validation

- CourierTrustLifecycleTests cover sequential progression, skipped/repeated transitions, activation prerequisites, malformed courier IDs, and invalid attribution fields.
- Full repository CI remains the validation gate for compilation and all tests.
