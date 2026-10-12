# Promise Commit Orchestration v1

## Flow

The application service runs the existing E7.2 outcome evaluator before calling Promise persistence.

- A resolved ACCEPTED result can persist only with the same requested delivery time.
- A resolved ALTERNATIVE_PROMISE result can persist only with the exact proposed alternative time.
- A resolved REJECTED result is returned without persistence.
- An UNRESOLVED result is returned without persistence.
- A mismatch between the evaluated result and commit proposal is returned without persistence.
- Structurally invalid proposals fail validation before persistence.

The PostgreSQL repository remains responsible for customer-scoped request lookup, atomic state/version/outbox writes, and replay behavior.

## Boundary

The service uses caller-supplied E7.1 criteria and alternative-search evidence. It does not independently verify evidence provenance, calculate ETA or safety buffer, authorize callers, expose an API endpoint, create a Mission, or dispatch outbox events.
