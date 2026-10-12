# Atomic Promise Commit v1

## Commit behavior

A Promise is scoped to one customer-owned delivery request. Its current row is updated together with an immutable version record and an outbox event in one PostgreSQL transaction.

- The first Promise version is 1.
- Each revision must use the same Promise ID and the next consecutive version.
- A repeated decision ID with an identical canonical proposal returns the original event as a replay.
- Reusing a decision ID with different proposal data, or submitting an unexpected version, returns a conflict.
- A request outside the authenticated customer scope is reported as not found.
- Version 1 emits `promise.committed`; later versions emit `promise.revised`.

The outbox uses the standard Mono event envelope and keeps publication state for a future worker. The repository does not publish events directly.

## Atomic boundary

The current Promise snapshot, immutable version evidence, and outbox event commit or roll back together. A failed database operation cannot leave a Promise without its corresponding event or an event without its Promise.

## Boundary

This operation requires a caller-supplied proposal that already passed structural validation. It does not prove the source feasibility result, calculate or endorse the safety buffer, authorize the caller, create a Mission, or expose an API endpoint. Delivery feasibility and promise-policy calculations remain upstream.
