# Sprint 251 — Courier offer refresh

## Goal

Reduce missed courier offers before external push delivery is configured, while avoiding background polling.

## Delivered

- Poll the authenticated courier session and offer/active-mission APIs every 15 seconds only while the courier app is visible and no mission is active.
- Refresh immediately when the app returns to the foreground.
- Prevent timer and foreground refresh from overlapping an in-flight request.
- Keep manual refresh available and show the refresh cadence in the interface.
- Preserve the map instance when an offer's route coordinates have not changed.
- Add test coverage for visible/hidden refresh behavior.

## Boundary

This is polling, not push delivery. It does not send browser notifications and does not run in the background. New-offer delivery is bounded by the polling interval when the app remains foregrounded.

## Verification

Run `npm test && npm run build` in `src/Mono.CourierMobile`; GitHub CI runs the full repository build and tests.
