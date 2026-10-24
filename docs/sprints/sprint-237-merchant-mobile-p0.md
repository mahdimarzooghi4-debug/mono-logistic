# Sprint 237 — Merchant Mobile P0

## Goal

Deliver a separate, installable mobile client for the merchant P0 core journey in Figma node `358:3`: home, shipments, shipment details, create shipment, and review/confirm the current quote and promise.

## Implemented

- Added `src/Mono.MerchantMobile` as a separate React/TypeScript/Vite app with an RTL mobile shell, bottom navigation, app manifest, icon, home, delivery filters, order/request ID search, branch selector, create flow, detail sheet, quote review and confirmation.
- Connected screens to existing authenticated branch, delivery request, detail, and mission-confirmation endpoints.
- Kept bearer token in memory only and exposed an explicit disconnect action.
- Added retry-safe idempotency key reuse for identical create and confirmation requests.
- Avoided invented business data; notification inbox reports the missing API contract.
- Added component tests for empty unauthenticated state, real API-backed home/list, request creation/idempotency, and quote/promise confirmation.

## Verification

- `npm test`: 4 tests passed.
- `npm run build`: TypeScript and Vite production build passed.

## Remaining product/integration gaps

- Production merchant identity flow must replace the development token field with approved short-lived OAuth/OIDC access.
- Recipient-name search requires an API contract; current list endpoint only returns order reference and request ID.
- Notifications require a merchant-scoped inbox endpoint and delivery mechanism.
- SLA risk requires an authoritative API field/feed; client only interprets statuses that explicitly encode risk.
- `VITE_SUPPORT_PHONE` and an HTTPS app origin remain environment configuration.
- PWA meets the current standalone mobile-web interpretation; a native Android/iOS package has not been requested or created.
