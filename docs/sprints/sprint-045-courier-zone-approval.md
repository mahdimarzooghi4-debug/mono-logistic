# Sprint 045 — Courier Zone Approval Model

- Status: In progress
- Phase: B — Logistics Core
- Epic: E6 — Zone & Capacity
- Capability: E6.2 — Courier Zone Approval
- Branch: sprint-045-courier-zone-approval
- Base: sprint-044-zone-model

## Sprint goal

Add a structural domain contract for courier preferred zones, Mono-approved zones, and an optional primary zone, without inventing geography, lifecycle, or operational authorization behavior.

## Scope

- Require a courier identifier.
- Validate preferred and approved zone IDs against a supplied valid zone registry.
- Reject blank or duplicate zone identifiers.
- Require approved zones to be selected from preferred zones.
- If present, require the primary zone to be approved.
- Keep the approval list as the stated operating boundary while documenting that runtime point/mission enforcement awaits geometry support.

## Acceptance criteria

1. Valid preferences and an approved subset pass.
2. Unknown, blank, or duplicate zone IDs are rejected.
3. Approved zones outside the preferred set are rejected.
4. A primary zone, when present, must be approved.
5. Empty preferences and approvals are valid and do not imply eligibility.
6. The domain contract does not infer zone kind or status and does not evaluate location geometry.
7. No persistence, approval API/workflow, audit, courier authorization, capacity, assignment, Stage, or deployment is added.

## Out of scope

Preference editing workflow, human approval authority, audit history, active-zone lifecycle, geometry formats or point-in-zone decisions, live courier eligibility, persistence, API endpoints, capacity, Stage, and deployment.
