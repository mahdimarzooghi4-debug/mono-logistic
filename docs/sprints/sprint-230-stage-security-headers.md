# Sprint 230 — Browser Security Headers

## Goal

Add low-risk browser response protections to the API-hosted Operations Console and verify them before Stage.

## Delivered

- Add `X-Content-Type-Options: nosniff` to API, health, and static-file responses.
- Add `X-Frame-Options: DENY` to prevent the console and API responses from being framed.
- Add `Referrer-Policy: strict-origin-when-cross-origin`.
- Extend health endpoint tests to assert all three headers.
- Update the Stage runbook and product backlog.

## Verification

Mono CI runs the API tests and the existing application, migration, and container smoke checks.

## Boundary

No content security policy or browser capability restriction is added, since those require reviewing the console's future map and location integrations. No Render resource or deployment is involved.
