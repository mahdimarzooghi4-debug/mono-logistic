# Sprint 060 — Promise Read API

- Status: In progress
- Phase: B — Logistics Core
- Epic: E7 — Feasibility & Promise
- Capability: E7.3 — Promise query
- Branch: sprint-060-promise-read-api
- Base: sprint-059-promise-commit-orchestration

## Scope

- Expose the current committed Promise through an authenticated read endpoint.
- Reuse delivery-requests.read authorization.
- Scope lookups by authenticated customer.
- Return a minimal response that omits internal buffer and decision evidence.
- Verify owner, foreign tenant, absent Promise, and insufficient-scope behavior.

## Acceptance criteria

1. An authorized tenant can read the current Promise for its delivery request.
2. A foreign tenant receives the same not-found response as an absent Promise.
3. Missing Promise returns 404.
4. A credential without delivery-requests.read is forbidden.
5. Internal safety-buffer values, policy versions, and reason codes are not serialized.
6. No Promise write, Mission creation, or external integration is added.

## Out of scope

Promise creation API, evidence provenance, ETA or buffer calculation, Mission lifecycle, outbox delivery, Stage, and deployment.
