# Sprint 253 — Courier session expiry handling

## Goal

Recover clearly from an invalid or expired courier token without persisting credentials in browser storage or issuing repeated unauthorized polling requests.

## Delivered

- Preserve API HTTP status in typed courier API errors.
- On HTTP 401, clear the in-memory token and mission data, stop foreground polling, and return to the connection screen with a clear reconnect message.
- Apply the same expiry handling to refresh, offer response, and mission lifecycle actions.
- Align the Courier Mobile README with the implemented OIDC boundary, polling behavior, active mission restoration, and external map handoff.
- Add regression coverage for token clearing and polling shutdown.

## Boundary

The app still uses a manually provided development token. This sprint does not add OIDC, refresh tokens, cookies, or browser credential persistence. OIDC remains an external identity integration requirement.

## Verification

Run `npm test && npm run build` in `src/Mono.CourierMobile`; GitHub CI runs the full repository build and tests.
