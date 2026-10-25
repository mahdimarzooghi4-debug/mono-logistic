# Sprint 249 — Courier offer map preview

## Goal

Ship the first self-contained mobile courier client slice that consumes the existing courier offer and privacy-limited route-context API.

## Included

- Manual development bearer token, held in page memory only.
- Session verification, active-offer fetch, and manual refresh.
- MapLibre GL map configured only by `VITE_MAP_STYLE_URL`; no public tile fallback.
- Pickup/dropoff markers only for valid API coordinates when `coordinates_status` is `COMPLETE`.
- Explicit pending-coordinate and map-not-configured states.
- Accept/decline API actions with per-offer/action idempotency keys retained through retries.
- Vitest coverage and a dedicated CI workflow.

## Deferred external/product integration

- Production courier OIDC login and short-lived token issuance.
- Stage map style/tile service URL and tile-service availability checks.
- Push/poll delivery of new offers.
- Navigation handoff, route line, ETA, and mission lifecycle actions after acceptance.

## Verification

Run `npm ci && npm test && npm run build` from `src/Mono.CourierMobile`. Do not deploy or merge as part of this sprint implementation.
