# Mono Operations Console

Read-only Persian RTL operations workbench for the Mono operations API. The page follows the Operations Console direction in Figma and uses live API responses; it does not contain fixture rows or invented KPI totals.

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
- Read-only courier directory with cursor pagination via `GET /operations/v1/couriers`, plus courier profile and trust-transition details via `GET /operations/v1/couriers/{courierId}`.
- Courier read-audit inspection via `GET /operations/v1/courier-read-audit`; the current API has no courier filter, so the UI only shows matching entries from its latest 100 records and labels an empty result accordingly.
- Zone capacity view with planned courier counts by 15/30/60-minute bucket via `GET /operations/v1/courier-capacity-projection`, alongside registered work windows from `GET /operations/v1/courier-availability-windows`.
- Read-only Mission fare review via `GET /operations/v1/finance/mission-fares`, joined to the exact immutable quote snapshot committed to that Mission. Shows recorded fare and buyer/seller split plus estimates using the Phase 1 rules: Mono commission 10% of fare, VAT 10% of commission, courier net 89% of fare. Reads are administrator-only and recorded in Mission operations read audit.
- Capacity requests convert browser-local time inputs to UTC and validate the API's aligned, maximum-24-hour range. A zone's active status is validated by the API.
- Schedule a courier work window for an entered courier ID, zone, and time range through the existing administrator-only API. The API enforces active courier status, approved active zone, no overlap, a 30-day horizon, 16-hour maximum duration, and idempotency. Cancel a scheduled window with a required reason and an idempotency key.
- Loading, empty, invalid-response, authentication, and API error states.

The bearer token remains in React memory and is cleared on reload or when the operator disconnects. No token is written to local storage, cookies, analytics, or logs. Use only a trusted local machine and HTTPS for any non-local API. Production login/session issuance is intentionally not included because the operations identity provider has not been selected or configured.

## Not included

The fare review is an estimate derived from recorded quote data and documented Phase 1 assumptions. It does not show or create actual charges, collections, balances, wallet entries, withdrawals, courier payouts, tax remittances, or settlements. It cannot confirm that Hana or any other customer paid.


This workbench does not mutate Mission or courier trust state, edit zones, dispatch/re-dispatch couriers, expose live location, or use presence/eligibility signals. Availability scheduling and cancellation are limited to the API's existing safeguards and are not dispatch authorization. Planned capacity is not real-time availability or dispatch authorization. Courier list search is not available in the current API. It does not include map/GPS providers, deployment, or server hosting.
