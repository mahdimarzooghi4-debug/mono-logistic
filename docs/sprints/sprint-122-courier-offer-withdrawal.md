# Sprint 122 — Courier offer withdrawal

## Goal

Allow Mono operations to release a pending courier offer while keeping the Mission in courier search.

## Delivered

- Added append-only `WITHDRAWN` offer event and projection status.
- Added an administrator-only idempotent withdrawal command:
  `POST /operations/v1/missions/{missionId}/courier-offers/{offerId}/withdraw`.
- The database verifies the offer belongs to the path Mission, is still offered, and the Mission is still searching for a courier.
- Withdrawal frees the active-offer slot so operations can offer the Mission to another courier.
- Added PostgreSQL integration coverage for first withdrawal, replay, and replacement offer creation.

## Boundary

A withdrawn offer cannot later be accepted or declined. This change does not automatically withdraw offers on courier suspension; that remains follow-up work. No GPS/OSM, Stage, or deployment behavior is added.
