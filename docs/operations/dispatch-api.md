# Dispatch operations API

All operations routes require the Mono administrator authorization policy.

## Dispatch queue

`GET /operations/v1/dispatch/queue?page_size=50&has_offer=true&deadline_passed=true&latest_action=EXPIRED&query=mis_`

The response contains the active offer (if any), the number of offers created for each Mission, and a summary of its latest attempt. An active offer includes `expires_at` and `deadline_passed`. The latter is computed against the API's UTC clock:

- `false`: the response window is still open.
- `true`: the response deadline has passed, but the offer remains `OFFERED` until an authorized expiry action is recorded.
- `active_offer: null`: no offer is currently active. Use `latest_attempt` to see the most recent outcome and `attempt_count` to see how many offers were made.

Use the optional `deadline_passed` query filter to return only Missions with an active offer whose deadline is in the requested state. Set it to `true` to focus on offers ready for the expiry action or `false` to see active offers with time remaining. The filter uses one API-clock timestamp for the page and its cursor validation.

Use the optional `latest_action` filter to focus on Missions by their most recent courier-offer action. Supported values are `OFFERED`, `ACCEPTED`, `DECLINED`, `WITHDRAWN`, `EXPIRED`, `RELEASED`, `CANCELLED`, and `COMPLETED`. The value is case-sensitive. Missions without an offer attempt do not match a supplied action. The filter is bound to cursor validation, so continue pagination with the same filters.

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
