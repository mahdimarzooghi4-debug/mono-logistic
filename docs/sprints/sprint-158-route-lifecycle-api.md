# Sprint 158 — Route plan operations API

## Goal

Expose the Route plan lifecycle already supported by the planning core through a protected operations API.

## Endpoints

- `POST /operations/v1/routes/{routeId}/plan` initializes version 1 through the existing confirmation, evidence-verification, simulation, idempotency, and persistence orchestration.
- `GET /operations/v1/routes/{routeId}/plan` returns the current immutable plan version.
- `GET /operations/v1/routes/{routeId}/plan/versions/{version}` returns a historical immutable version.

All endpoints require the MFA-gated Mono operations administrator policy. Initialization accepts an explicit command ID, route origin and start time, Mission, service durations, and signed matrix evidence. It cannot create a Promise, select a courier, dispatch, or bypass the verified-evidence gate.

## Outcomes

- A new plan returns `201 Created` and links to version 1.
- An identical idempotent replay returns `200 OK`; a changed command conflict returns `409 Conflict`.
- An unavailable confirmed Mission returns `404 Not Found`.
- Unresolved evidence or an infeasible time window returns `422 Unprocessable Entity`.
- Missing current or historical plans return `404 Not Found`.

## Acceptance

- Public callers cannot read or initialize Route plans.
- Every write is delegated to `InitialRoutePlanOrchestrationService`; the API contains no planning or persistence bypass.
- Current and historical plan reads use the immutable version store.
