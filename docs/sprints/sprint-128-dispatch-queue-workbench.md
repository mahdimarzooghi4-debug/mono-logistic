# Sprint 128 — Dispatch queue workbench

## Goal

Give Mono operations a single read-only workbench view of missions waiting for courier dispatch, including whether an unanswered courier offer is already active. This supports safe replacement offers after declines, withdrawals, and pre-handoff reassignment.

## API

`GET /operations/v1/dispatch/queue`

Requires the operations administrator authorization policy.

Query parameters:

- `page_size`: 1–100, defaults to 50.
- `has_offer`: optional boolean to include only missions with or without a pending `OFFERED` courier offer.
- `query`: optional case-insensitive prefix search across mission ID or delivery request ID.
- `cursor`: optional mission ID from the previous page; it must still match the same filters and remain in the dispatch queue.

The response is ordered by newest mission first and includes mission/customer/request identifiers, committed delivery time, creation time, and optional active offer details. A next cursor is returned when more results exist. Each read is recorded as `MISSION_DISPATCH_QUEUE` in the append-only operations read audit.

## Boundaries

- The queue contains only missions in `SEARCHING_COURIER`.
- An active offer means a courier has not yet accepted or declined the offer.
- This endpoint is a read model; it does not create, withdraw, accept, or release offers.
- Results contain no courier location or customer personal data.

## Verification

Integration coverage checks the queue before and after offer creation, both `has_offer` filter paths, and recording the dispatch queue audit action.
