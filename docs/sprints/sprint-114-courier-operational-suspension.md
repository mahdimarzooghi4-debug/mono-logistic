# Sprint 114 — Audited courier suspension and reinstatement

## Goal

Give Mono administrators a safe, auditable control to suspend an ACTIVE courier and later reinstate that courier.

## Scope

- Keep operational suspension separate from the ordered courier trust lifecycle.
- Require an MFA-authorized Mono administrator, a canonical reason code, and an Idempotency-Key.
- Persist append-only SUSPENDED and REINSTATED events in PostgreSQL.
- Expose current operational status and the latest 50 status events through the Mono operations API.
- Include suspension state when accepting planned availability windows and calculating planned capacity.
- Fail closed on invalid transitions and keep retries idempotent.

## Acceptance criteria

- Only trust-status ACTIVE couriers can be suspended or reinstated.
- Reinstatement is valid only after a suspension; repeated suspension is rejected.
- Exact retries replay the original resulting status; changed input with the same key conflicts.
- Status reads record a privileged courier detail audit entry before returning data.
- Database constraints reject invalid event transitions and mutations to event/idempotency history.
- Suspension atomically cancels every scheduled window that ends after the suspension timestamp; those windows stay cancelled after reinstatement.
- Suspended couriers cannot receive new planned windows and are excluded from planned capacity counts.
- API responses contain operational metadata only.

## Boundary

This sprint does not authenticate couriers, collect GPS, start dispatch, or deploy to Stage or production. Trust status remains ACTIVE during an operational suspension so trust review history is preserved independently.

## Validation

PostgreSQL-backed integration coverage validates transitions, idempotency, status history, immutable audit data, cancellation of scheduled windows during suspension, and creation of fresh availability after reinstatement. Full CI builds, tests, and builds the container image.
