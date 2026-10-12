# Sprint 073 — Promise commit HTTP API

## Goal

Expose the existing E7.2 Promise outcome evaluator and Promise commit application service through the authenticated B2B API.

## Contract

- Add `POST /v1/delivery-requests/{id}/promise`, protected by `delivery-requests.write`.
- Require exactly one valid `Idempotency-Key`; use it as the commit decision ID.
- Accept the requested feasibility criteria, optional alternative search, and a proposal envelope. The request path supplies the delivery request ID.
- Validate canonical enum values and the proposal before calling the application service.
- Reuse the existing E7.2 evaluator, evidence-integrity contract, tenant-bound source verifier, and atomic PostgreSQL commit.
- Return the existing safe current-Promise response after commit or exact replay. Map conflicts, missing requests, unresolved/rejected outcomes, proposal mismatch, and evidence failures to stable API errors.
- Keep production evidence-source verification fail-closed. No feasibility rule, trusted provider, external API, Mission creation, Stage, or deployment is introduced.

## Verification

The PostgreSQL-backed HTTP test sends an authenticated request, repeats it with the same key and payload, and changes the payload under the same key. It verifies one persisted Promise version, a stable replay response, conflict on changed input, source-verifier invocation, and omission of internal safety-buffer and policy evidence from the public response.
