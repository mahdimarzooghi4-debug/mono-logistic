# Sprint 117 — Mono-managed courier identity binding

## Goal

Bind a validated OIDC issuer subject to Mono's courier registry without trusting a courier identifier supplied by the JWT issuer.

## Delivered

- Add a Mono-owned current binding projection and append-only bind/revoke event history.
- Restrict bindings to the configured HTTPS `MonoCourier:Authority`; no arbitrary issuer can be added through the operations API.
- Require the MFA-gated Mono operations administrator policy for bind, revoke, and binding-summary reads.
- Require idempotency keys, reason codes, and opaque evidence references for identity-binding writes.
- Enforce active, unsuspended courier state and unique active issuer+subject ownership in PostgreSQL.
- Resolve the exact validated token issuer and unique `sub` through Mono's current binding on each token validation.
- Reject issuer-supplied `mono/courier_id` and `mono/identity_domain` claims; add Mono's internal claims only after successful resolution.
- Return binding status and operational metadata without exposing issuer or subject in the operations API response.

## API

```
POST /operations/v1/couriers/{courierId}/identity-binding
POST /operations/v1/couriers/{courierId}/identity-binding/revoke
GET  /operations/v1/couriers/{courierId}/identity-binding
```

Binding and revocation writes require `Idempotency-Key`, `reason_code`, and an opaque `evidence_reference`. The bind request also carries the issuer subject. Summary responses omit issuer and subject.

## Validation

PostgreSQL integration coverage exercises binding, exact replay, changed-request conflict, duplicate-subject rejection, revoked binding rejection, and inactive/suspended courier rejection. CI must pass for the exact branch head.

## Remaining boundary

No issuer or audience, mobile verification, OTP delivery, token provisioning, or device binding is configured. The courier authentication scheme remains fail-closed until its authority and audience are configured. This sprint does not add GPS, location retention, assignment, dispatch, Stage, or deployment.
