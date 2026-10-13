# Dispatch operations API

All operations routes require the Mono administrator authorization policy.

## Dispatch queue

`GET /operations/v1/dispatch/queue?page_size=50&has_offer=true&deadline_passed=true&query=mis_`

The response contains the active offer (if any), the number of offers created for each Mission, and a summary of its latest attempt. An active offer includes `expires_at` and `deadline_passed`. The latter is computed against the API's UTC clock:

- `false`: the response window is still open.
- `true`: the response deadline has passed, but the offer remains `OFFERED` until an authorized expiry action is recorded.
- `active_offer: null`: no offer is currently active. Use `latest_attempt` to see the most recent outcome and `attempt_count` to see how many offers were made.

Use the optional `deadline_passed` query filter to return only Missions with an active offer whose deadline is in the requested state. Set it to `true` to focus on offers ready for the expiry action or `false` to see active offers with time remaining. The filter uses one API-clock timestamp for the page and its cursor validation.

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

## Attempt history

`GET /operations/v1/missions/{missionId}/dispatch-attempts` returns up to 100 offers newest first, with each offer's current status and latest event action, actor, reason, and timestamp. Successful reads are recorded as `MISSION_DISPATCH_ATTEMPT_HISTORY` audit events. The per-offer `/events` route returns the full event sequence.
