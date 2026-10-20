# Mono Merchant Portal

Persian, RTL merchant web portal for customer-scoped Mono delivery APIs. This slice implements the Figma merchant dashboard's operationally supported screens: dashboard summary, new urban delivery request, paged delivery list, delivery detail/tracking, and active branches.

## Local development

Requirements: Node.js 20.19+ or 22.12+.

```sh
npm ci
npm run dev
```

The Vite server listens on `127.0.0.1:5173` and proxies `/v1/*` to `http://localhost:5000`. Start `Mono.Api`, then connect with a Mono customer API key that has `branches.read`, `delivery-requests.read`, and `delivery-requests.write` scopes. The key remains in React memory and is cleared when disconnected or on reload. It is not saved to storage, cookies, or logs. Production hosting must use HTTPS and a same-origin reverse proxy for `/v1/*`.

## Connected behavior

- Active branch list: `GET /v1/branches`.
- Delivery request list with API cursor pagination: `GET /v1/delivery-requests?page_size=25&cursor=...`.
- Delivery request create with a browser-generated `Idempotency-Key`: `POST /v1/delivery-requests`.
- Read-only delivery detail and recorded quote/promise: `GET /v1/delivery-requests/{deliveryRequestId}`.
- Dashboard counts are calculated only from the currently loaded page. The UI does not invent organization-wide totals.
- Loading, empty, authentication, response-contract, and API error states are shown explicitly.

The create form follows the current Phase 1 API contract: urban delivery, standard priority, bag-based shipment, and load classes S/M/L. It does not submit guessed coordinates or create a price/delivery promise. Address text alone is not geocoded by the current API; when no coordinates are available, the response reports pending route coordinates. Quote issuance requires verified distance evidence, so the portal only displays a quote already returned by the API.

Actual billing, collections, account settings, branch editing, and payment are not part of this slice. The Figma screens for these areas remain design references until their APIs and external providers are ready.
