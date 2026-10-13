# Sprint 132 — Overdue offer queue guidance

## Goal

Help operations distinguish a live response window from an overdue offer and document the exact expiry action.

## Delivered

- Add `active_offer.deadline_passed` to the dispatch queue response, calculated using the API UTC clock.
- Keep the offer status as `OFFERED` until the explicit expiry command appends its immutable `EXPIRED` event.
- Test both a not-yet-due offer and a deadline that has passed before the expire command runs.
- Add the dispatch API contract covering queue fields, authorization, idempotency, expiry outcomes, and the absence of a scheduled worker.
