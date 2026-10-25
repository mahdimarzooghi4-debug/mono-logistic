# Sprint 255 — Consented foreground courier location

## Goal

Connect the existing courier current-location API to the mobile mission flow with explicit courier consent and a bounded, foreground-only lifecycle.

## Delivered

- Add a visible opt-in control on active missions before requesting browser geolocation.
- Publish the authenticated device location no more frequently than every 15 seconds while the mission is active and the app is visible.
- Pause the browser watch in the background; resume only while the courier's in-memory consent remains active.
- Stop location sharing when the courier stops it, the mission ends, or the app session changes.
- Add authenticated `DELETE /courier/v1/location` to clear only the calling courier's current point.
- Request server-side clearing when location sharing stops or the foreground-only watcher is torn down.
- Verify mobile consent and payload behavior, plus PostgreSQL deletion isolation for one courier.

## Boundary

The database still stores only the current point, not a trail. Foreground updates do not continue while an external navigation app is in the foreground. Server-side deletion is best-effort if the device loses connectivity or is force-closed. This sprint does not add a customer/merchant read endpoint; only the authenticated courier can write or clear its own point.

## Verification

Run `npm test && npm run build` in `src/Mono.CourierMobile` and full .NET tests in GitHub CI.
