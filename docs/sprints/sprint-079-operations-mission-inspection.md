# Sprint 079 — Operations Mission inspection

## Goal

Give authenticated Mono operations staff a safe, read-only view of Mission work across customers.

## Changes

- Add MFA-gated operations routes for paginated Mission search, Mission detail, and Mission timeline.
- Support an optional canonical status filter and keyset pagination by creation time and Mission ID.
- Reuse the allowlisted timeline envelope; do not return internal event payloads, recipient details, or courier personal data.
- Add input validation tests and document the route contract.

## Security and scope

All routes require the existing Mono administrator authorization policy, which requires a validated operations token with MFA. Customer identity is never accepted from query or body input. The feature is read-only and introduces no Mission transitions, event publishing, Stage work, or deployment.

## Validation

CI will build and run the API test suite on this Draft PR.
