# Mono Operations Console

Read-only Persian RTL dispatch workbench for the Mono operations API. The page follows the Operations Console direction in Figma and uses live API responses; it does not contain fixture rows or invented KPI totals.

## Local development

Requirements: Node.js 20.19+ or 22.12+.

```sh
npm ci
npm run dev
```

The Vite development server listens on `127.0.0.1:5173` and proxies `/operations/*` to `http://localhost:5000`. Start `Mono.Api` on that address first, then open the console and paste a valid Mono operations bearer token. For a different local API port, update the development proxy in `vite.config.ts`; the browser always sends the token to its own origin.

## Implemented in this slice

- MFA-protected dispatch queue read via `GET /operations/v1/dispatch/queue`.
- Searchable, date-filtered Mission directory via `GET /operations/v1/missions`.
- Prefix search, exact customer scope, active-offer filter, delivery-window filter, created-before filter, due/creation ordering, and cursor pagination.
- Per-page summaries calculated only from the current response page.
- Read-only Mission details, lifecycle timeline, and read-audit history via `GET /operations/v1/missions/{missionId}`, `GET /operations/v1/missions/{missionId}/timeline`, and `GET /operations/v1/mission-read-audit?mission_id=...`.
- Loading, empty, invalid-response, authentication, and API error states.

The bearer token remains in React memory and is cleared on reload or when the operator disconnects. No token is written to local storage, cookies, analytics, or logs. Use only a trusted local machine and HTTPS for any non-local API. Production login/session issuance is intentionally not included because the operations identity provider has not been selected or configured.

## Not included

This workbench does not mutate Mission state, dispatch/re-dispatch couriers, expose live location, or show route/zone/finance screens. It does not include map/GPS providers, deployment, or server hosting.
