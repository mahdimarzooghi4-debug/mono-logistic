# Mono Operations Console

Persian RTL operations workbench for the Mono operations API. Read screens use live API responses and avoid fixture rows or invented KPI totals. A small set of administrator actions is available only through APIs that enforce authorization, MFA, validation and audit.

## Local development

Requirements: Node.js 22.22+ or 24.15+.

```sh
npm ci
npm run dev
```

The Vite development server listens on `127.0.0.1:5173` and proxies `/operations/*` to `http://localhost:5000`. Start `Mono.Api` on that address first, then open the console and paste a valid Mono operations bearer token. For a different local API port, update the development proxy in `vite.config.ts`; the browser always sends the token to its own origin.

## Implemented in this slice

- MFA-protected dispatch queue read via `GET /operations/v1/dispatch/queue`.
- Administrator-only delivery SLA alert feed via `GET /operations/v1/sla/delivery-alerts`, with 0/15/30/60/240-minute due windows, evaluated-at display, and cursor pagination. Counts are limited to the currently loaded page. Alert rows link to Mission details and timeline.
- Administrator-only courier presence roster via `GET /operations/v1/couriers/presence`, with ONLINE/OFFLINE and zone filters, cursor pagination, snapshot timestamps, page-scoped counts, and links to courier profiles.
- Administrator-only worker status dashboard for the Mission courier offer-expiry and webhook delivery workers. Shows enabled/state, latest attempt/success, batch counts, and per-process scope via `GET /operations/v1/dispatch/offer-expiry-worker/status` and `GET /operations/v1/webhook-deliveries/worker-status`. Status is in-process, not deployment-wide health, and refresh is manual.
- Administrator-only courier issue inbox via `GET /operations/v1/courier-issues`, filtered by status, issue code, Mission, courier, and reported-time window with cursor pagination. Shows only structured report metadata and links to Mission details; it does not resolve reports or expose free-text notes or location data.
- Searchable, date-filtered Mission directory via `GET /operations/v1/missions`.
- Prefix search, exact customer scope, active-offer filter, delivery-window filter, created-before filter, due/creation ordering, and cursor pagination.
- Per-page summaries calculated only from the current response page.
- Read-only Mission details, lifecycle timeline, and read-audit history via `GET /operations/v1/missions/{missionId}`, `GET /operations/v1/missions/{missionId}/timeline`, and `GET /operations/v1/mission-read-audit?mission_id=...`.
- Read-only courier directory with server-side courier ID prefix and trust status filters plus cursor pagination via `GET /operations/v1/couriers`, plus courier profile and trust-transition details via `GET /operations/v1/couriers/{courierId}`.
- Courier read-audit inspection via `GET /operations/v1/courier-read-audit`; the current API has no courier filter, so the UI only shows matching entries from its latest 100 records and labels an empty result accordingly.
- Zone capacity view with planned courier counts by 15/30/60-minute bucket via `GET /operations/v1/courier-capacity-projection`, alongside registered work windows from `GET /operations/v1/courier-availability-windows`.
- Read-only Mission fare review via `GET /operations/v1/finance/mission-fares`, joined to the exact immutable quote snapshot committed to that Mission. Shows recorded fare and buyer/seller split plus estimates using the Phase 1 rules: Mono commission 10% of fare, VAT 10% of commission, courier net 89% of fare. Reads are administrator-only and recorded in Mission operations read audit.
- Capacity requests convert browser-local time inputs to UTC and validate the API's aligned, maximum-24-hour range. A zone's active status is validated by the API.
- Schedule a courier work window for an entered courier ID, zone, and time range through the existing administrator-only API. The API enforces active courier status, approved active zone, no overlap, a 30-day horizon, 16-hour maximum duration, and idempotency. Cancel a scheduled window with a required reason and an idempotency key.
- Administrator-only policy registry workbench via `/operations/v1/policies`: list and inspect immutable versions, create policies and Draft versions, inspect policy audit history, activate Draft versions, and retire Active/Inactive versions. Lifecycle actions require a non-empty reason; the API requires a Mono administrator token with verified MFA and records lifecycle audit events. Uncertain create responses must be verified from the registry before retrying because policy creation does not currently accept an idempotency key.
- Effective-policy resolution preview via `GET /operations/v1/policies/effective` and bounded HARD evaluation via `POST /operations/v1/policies/evaluate`. These are previews only: activation changes registry status and does not apply policy to delivery requests or Missions. Non-HARD behavior is not evaluated.
- Administrator-only customer and branch onboarding via the existing APIs: paginated customer queue and prefix search, Pending customer creation, reasoned activation confirmation, branch registration/editing for active customers, and customer/branch audit inspection. Customer registration does not issue credentials; every mutation remains API-authorized and audit-backed.
- Loading, empty, invalid-response, authentication, and API error states.

For Stage, the production Vite assets are built into the API container and served from the same HTTPS origin at `/`; browser API requests stay same-origin. Local development continues to use the Vite proxy.\n\nThe bearer token remains in React memory and is cleared on reload or when the operator disconnects. No token is written to local storage, cookies, analytics, or logs. Use only a trusted local machine and HTTPS for any non-local API. Production login/session issuance is intentionally not included because the operations identity provider has not been selected or configured.

## Not included

The fare review is an estimate derived from recorded quote data and documented Phase 1 assumptions. It does not show or create actual charges, collections, balances, wallet entries, withdrawals, courier payouts, tax remittances, or settlements. It cannot confirm that Hana or any other customer paid.


Worker status is process-local; in a multi-instance deployment, each API instance must be inspected separately. Webhook replay/cancellation, courier availability scheduling/cancellation and policy lifecycle are explicit administrator actions whose eligibility remains authoritative on the API. Webhook outcomes and policy lifecycle are audited. No webhook payload, callback URL, or signing secret is displayed. Policy resolution/evaluation does not apply decisions to delivery intake or Missions. This workbench does not mutate Mission or courier trust state, edit zones, dispatch/re-dispatch couriers, expose live location, or treat presence/eligibility as dispatch authorization. Planned capacity is not real-time availability or dispatch authorization. It does not include map/GPS providers, identity-provider configuration, or operational API credentials.

## Verification

```sh
npm ci
npm run build
npm test
```

The policy workbench tests cover bearer-token forwarding, registry/detail/audit reads, the administrator MFA-denial state, and non-executing HARD evaluation previews. The onboarding workbench tests cover customer reads and audit, Pending registration, reasoned activation, branch validation and creation, and administrator/MFA denial.
