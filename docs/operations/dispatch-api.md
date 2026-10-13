# Dispatch operations API

All operations routes require the Mono administrator authorization policy.

## Dispatch queue

`GET /operations/v1/dispatch/queue?page_size=50&has_offer=true&deadline_passed=true&delivery_after=2026-10-13T16:00:00Z&delivery_before=2026-10-13T18:00:00Z&delivery_window_state=upcoming&customer_id=cus_0123456789abcdef0123456789abcdef&last_attempt_before=2026-10-13T17:30:00Z&delivery_overdue=true&sort_by=delivery_due&min_attempt_count=2&max_attempt_count=4&latest_action=EXPIRED&query=mis_`

The response contains the active offer (if any), the number of offers created for each Mission, a summary of its latest attempt, and `delivery_window_state`, computed from the same UTC timestamp captured for the request. Its values are `upcoming` before the committed delivery time, `due_now` at the exact timestamp, and `overdue` after it. The existing `delivery_overdue` boolean remains inclusive and is `true` at the exact timestamp, so it is also true when `delivery_window_state` is `due_now`. Use the optional `delivery_window_state` query filter to return only one of these three states. It is evaluated against the same request timestamp and is applied during cursor validation; keep it unchanged throughout pagination.

Use the optional `customer_id` filter to restrict the queue to one exact customer ID (for example, `cus_0123456789abcdef0123456789abcdef`). Omit it for the full Mono operations queue. The ID is validated and the exact customer scope is applied during cursor validation and every page query; keep it unchanged while paging.

Use `delivery_due_within_minutes` to focus on deadlines from the request's UTC timestamp through the specified number of minutes ahead. Both endpoints are inclusive: a Mission due exactly now or exactly at the horizon endpoint matches; past deadlines do not. Values must be from 1 to 10,080 (seven days). The horizon is recalculated from the UTC clock for each request, so keep the parameter unchanged while paging; a Mission that crosses a deadline during pagination can change its state and cease to match. An active offer includes `expires_at` and `deadline_passed`. The latter is computed against the API's UTC clock:

- `false`: the response window is still open.
- `true`: the response deadline has passed, but the offer remains `OFFERED` until an authorized expiry action is recorded.
- `active_offer: null`: no offer is currently active. Use `latest_attempt` to see the most recent outcome and `attempt_count` to see how many offers were made.

Use the optional `deadline_passed` query filter to return only Missions with an active offer whose deadline is in the requested state. Set it to `true` to focus on offers ready for the expiry action or `false` to see active offers with time remaining. The filter uses one API-clock timestamp for the page and its cursor validation.

Use the optional `latest_action` filter to focus on Missions by their most recent courier-offer action. Supported values are `OFFERED`, `ACCEPTED`, `DECLINED`, `WITHDRAWN`, `EXPIRED`, `RELEASED`, `CANCELLED`, and `COMPLETED`. The value is case-sensitive. Missions without an offer attempt do not match a supplied action. The filter is bound to cursor validation, so continue pagination with the same filters.

Use the optional `delivery_before` filter to return Missions whose committed delivery time is at or before the supplied ISO 8601 timestamp. Send an explicit UTC timestamp (for example, `2026-10-13T18:00:00Z`); the comparison is inclusive. This highlights Missions with earlier committed delivery promises without changing their state. Cursor validation applies the same cutoff, so keep it unchanged when requesting later pages.

Use the optional `delivery_after` filter to set an inclusive lower bound on committed delivery time. Combine it with `delivery_before` to select a delivery window; both values must be ISO 8601 timestamps, and `delivery_after` cannot be later than `delivery_before`. Cursor validation applies both bounds, so keep the complete window unchanged for every page.

Use the optional `delivery_overdue` filter to focus on searching Missions whose committed delivery time has passed relative to one UTC timestamp captured for the request. The response includes `delivery_overdue` for every item; the cutoff is inclusive, so it becomes `true` at the exact committed delivery time. The same value is used for cursor validation and all rows in the page. This is an operational risk signal and does not change Mission state or automatically expire an offer.

Use `min_attempt_count` and `max_attempt_count` to restrict results by the number of offers created for each Mission, including previous attempts. Both bounds are optional and inclusive; values must be non-negative, and the minimum cannot exceed the maximum. These filters are applied before pagination and during cursor validation, so use the same bounds on every page.

Use `last_attempt_before` to return Missions whose most recent courier-offer event occurred at or before an explicit UTC ISO 8601 timestamp. Missions with no offer history do not match this filter. The cutoff is inclusive and is applied during cursor validation; keep it unchanged for all pages.

Use `sort_by=delivery_due` to order the queue by earliest committed delivery time first. Equal due times are ordered by newest Mission creation time, then descending Mission ID, giving stable keyset pagination. Omit `sort_by` or use `sort_by=created_at` to preserve the existing newest-created-first order. Keep the sort and filters unchanged while following a cursor.

Queue reads are recorded as `MISSION_DISPATCH_QUEUE` audit events.

## Expire an overdue offer

After the queue reports `active_offer.deadline_passed: true`, operations can close that offer explicitly:

```http
POST /operations/v1/missions/{missionId}/courier-offers/{offerId}/expire
Idempotency-Key: expire-{offerId}
```

The key must be a single valid `Idempotency-Key` value. A successful call returns the offer with status `EXPIRED`; the immutable offer event records reason `OPERATIONS_EXPIRED_OFFER`. Repeating the same request with the same key replays the original result.

The API rejects an early expiry, an already-answered offer, or an offer whose Mission is no longer `SEARCHING_COURIER` with `409 MISSION_COURIER_OFFER_NOT_ALLOWED`. Unknown Missions/offers return 404; invalid IDs or keys return 400. A successful expiry does not create a replacement offer. Operations can issue a new offer after expiry using `POST /operations/v1/missions/{missionId}/courier-offers` and a new idempotency key.

Expiry remains an explicit operations action. No scheduled expiry worker is part of this API.

## Expire overdue offers in a batch

For a small set of overdue offers, operations can submit up to 20 commands in one request:

```http
POST /operations/v1/dispatch/queue/expire-overdue
Content-Type: application/json

{
  "commands": [
    {
      "mission_id": "mis_...",
      "offer_id": "mco_...",
      "idempotency_key": "expire-mco-..."
    }
  ]
}
```

Each command carries its own idempotency key. Duplicate offer IDs are rejected before any command runs. The API validates the complete batch first, then executes each command with the same expiry rules as the single-offer route. The response contains one outcome per command: `Applied`, `Replayed`, `OfferNotFound`, `MissionNotFound`, `Conflict`, or `InvalidState`. A batch may therefore contain successful and unsuccessful items; items are committed independently and are not rolled back as a group. Retry an interrupted request with the same commands and keys.

The batch route returns HTTP 400 for an invalid command list. An item with `InvalidState` means it was not expired, for example because its deadline has not passed or it has already been answered. Use the single-offer route when one HTTP status per operation is preferable.

## Create offers in a batch

Operations can explicitly offer up to 20 distinct searching Missions to selected couriers in one request:

```http
POST /operations/v1/dispatch/offers/batch
Content-Type: application/json

{
  "commands": [
    {
      "mission_id": "mis_...",
      "courier_id": "crr_...",
      "zone_id": "zone-central",
      "idempotency_key": "offer-mission-..."
    }
  ]
}
```

The full list is validated before execution. Duplicate Mission IDs are rejected, and each command requires its own Mission-scoped idempotency key. Items then use the regular offer rules and commit independently; the response contains one outcome per command, so partial success is possible. Retry with the original commands and keys. Couriers still explicitly accept or decline, and batch submission does not rank or select couriers.


`GET /operations/v1/missions/{missionId}/dispatch-attempts` returns up to 100 offers newest first, with each offer's current status and latest event action, actor, reason, and timestamp. Successful reads are recorded as `MISSION_DISPATCH_ATTEMPT_HISTORY` audit events. The per-offer `/events` route returns the full event sequence.
