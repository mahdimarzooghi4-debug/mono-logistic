# Sprint 254 — Courier installable shell

## Goal

Make Courier Mobile easier to exercise on a phone before Stage deployment and prepare an installable shell for HTTPS hosting.

## Delivered

- Listen on LAN interfaces in Vite development and preview commands.
- Add production service-worker registration and cache only the application shell and same-origin static assets.
- Explicitly bypass `/courier/` API requests so mission and identity responses are never cached.
- Add an offline fallback page that states which courier operations still require connectivity.
- Provide 192, 512, and Apple touch PNG icons and reference them from the manifest and HTML.
- Allow non-cryptographic idempotency identifiers only as a fallback for HTTP LAN development browsers that lack `crypto.randomUUID`; production browsers use `crypto.randomUUID`.
- Document LAN testing boundaries and HTTPS-based installation steps.

## Boundary

Offline mode does not support sign-in, offer refresh, navigation data retrieval, or mission updates. LAN HTTP is for trusted-network UI testing with development credentials only; it is not Stage or installation verification.

## Verification

Run `npm test && npm run build` in `src/Mono.CourierMobile`; confirm the built output includes the manifest icons, offline fallback, and service worker. GitHub CI runs full repository build and tests.
