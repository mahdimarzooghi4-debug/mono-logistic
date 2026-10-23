# Mono Merchant Mobile

Standalone, installable mobile web client for the merchant P0 flows in Figma node `358:3`. This is a separate app from `Mono.MerchantPortal`; its small-screen layout and bottom navigation are purpose-built for mobile.

## Run

```sh
npm ci
npm test
npm run build
npm run dev
```

The development server proxies `/v1` requests to `http://localhost:5000`. Set `VITE_API_BASE_URL` to a deployed API origin when serving the app separately. Installability requires HTTPS in the served environment.

## Connected behavior

- Reads active merchant branches from `GET /v1/branches`.
- Reads shipment cards from `GET /v1/delivery-requests?page_size=50` and details from `GET /v1/delivery-requests/{id}`.
- Creates requests at `POST /v1/delivery-requests` with an idempotency key retained across retry of the same payload.
- Confirms only a non-expired quote paired with a returned promise at `POST /v1/delivery-requests/{id}/confirm`, using the quote ID and promise version from the API.
- Access token stays in page memory and is discarded on disconnect/reload. Do not use a long-lived production credential as a substitute for merchant sign-in.

## Remaining integration requirements

- Production merchant sign-in: configure an appropriate short-lived OAuth/OIDC mobile client and customer-scoped authorization; the current API accepts customer bearer tokens, while this client currently offers a manual token field for connected development.
- The API list response has no recipient identity, so mobile search currently covers external order reference and delivery request ID. Add a privacy-scoped recipient search contract if recipient search is required.
- The API currently has no merchant notification inbox endpoint. The screen explicitly reports that gap; it does not fabricate notifications.
- A reliable SLA-risk field/event feed is needed to power the Figma's at-risk summary. The client only highlights statuses explicitly returned as risk/late by the API.
- Configure an approved support phone number with `VITE_SUPPORT_PHONE`.
- Confirm the supported PWA install/browser matrix and provision the final HTTPS application origin.
