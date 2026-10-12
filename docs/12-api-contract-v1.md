# Mono B2B API Contract v1

## 1. Purpose

Mono publishes a reusable B2B logistics API.

Customers consume Mono's API. Mono does not create a separate API core for each customer.

```
Customer System / Dashboard
  -> Mono API
  -> Shared Mono Core
```

Customer-specific behavior is resolved through Customer Configuration and Policies.

## 2. API Principles

- REST/JSON for synchronous B2B operations
- Webhooks for asynchronous lifecycle events
- versioned endpoints
- idempotent writes
- customer-scoped authorization
- stable external identifiers
- auditable responses
- no provider-specific map fields in core API
- no Hana-specific endpoint naming

## 3. Versioning

Base path:

```
/v1
```

Breaking contract changes require a new API version or explicitly versioned resource behavior.

## 4. Authentication

Recommended options:

- OAuth2 client credentials for enterprise customers
- API keys for controlled/simple integrations
- session authentication for Mono Dashboard users

Every request resolves:

- customer_id
- contract_id
- permissions
- service configuration
- policy scope

Implemented customer operations:

- `GET /v1/branches` requires `branches.read` and returns only the authenticated customer's branch summaries.
- `POST /v1/delivery-requests` requires `delivery-requests.write`; customer identity comes only from the credential.

## 5. Idempotency

All create/command endpoints should accept:

```
Idempotency-Key
```

Mono returns the original delivery request ID for a retry with the same key and normalized payload. Reusing the key for changed request content returns `409 IDEMPOTENCY_CONFLICT`. Keys are scoped per authenticated customer, 1–128 characters, and use only ASCII letters, digits, `.`, `_`, `~`, or `-`.

## 6. Create Delivery Request

```
POST /v1/delivery-requests
```

Required JSON fields:

- `external_reference` (1–128 characters)
- `branch_id` (an active branch belonging to the authenticated customer)
- `pickup` and `dropoff` location objects, each with an address or coordinate pair
- `shipment` (Phase 1: BAG_BASED, positive bag_count, positive optional weight, S/M/L load class; optional positive package_count and estimated_volume_m3; optional temperature_class AMBIENT/CHILLED/FROZEN)
- `requested_service`: `URBAN_DELIVERY`
- `requested_priority`: `STANDARD`
- optional `estimated_ready_at` timestamp

The `Idempotency-Key` header is mandatory. Customer IDs are never accepted in request content.

Example:

```json
{
  "external_reference": "ORDER-123",
  "branch_id": "br_0123456789abcdef0123456789abcdef",
  "pickup": {
    "address_text": "Store address",
    "latitude": 35.7000,
    "longitude": 51.4000,
    "contact_name": "Store",
    "contact_phone": "+98..."
  },
  "dropoff": {
    "address_text": "Customer address",
    "latitude": 35.7100,
    "longitude": 51.4200,
    "recipient_name": "Recipient",
    "recipient_phone": "+98...",
    "address_details": "Unit 4"
  },
  "shipment": {
    "shipment_model": "BAG_BASED",
    "bag_count": 2,
    "estimated_weight_kg": 8,
    "load_class": "M",
    "fragile": false,
    "liquid": true,
    "keep_upright": true,
    "package_count": 3,
    "estimated_volume_m3": 0.025,
    "temperature_class": "AMBIENT"
  },
  "requested_service": "URBAN_DELIVERY",
  "requested_priority": "STANDARD",
  "estimated_ready_at": "2026-09-27T12:10:00Z"
}
```

New shipment facts are optional in v1; volume is expressed in cubic metres. When omitted, they remain absent from the normalized canonical payload so existing idempotent retries keep matching the original request. No equality between bag_count and package_count is inferred.

The response has `delivery_request_id` and `status: RECEIVED`. A retry with the same key and normalized body returns the same values. No quote, feasibility decision, promise, or Mission is implied by receipt.

## 6.1 List Delivery Requests

```
GET /v1/delivery-requests?page_size=50&cursor=drq_...
```

Requires `delivery-requests.read`. The default page size is 50 and the maximum is 100. Results are ordered by creation time descending, then request ID descending. `next_cursor` is the last returned request ID and may be sent as `cursor` to fetch the next page. A cursor must belong to the authenticated customer; unknown and foreign-customer cursors return `400 INVALID_CURSOR`. The list contains request summaries only and excludes pickup/dropoff and contact data.

Response:

```json
{
  "items": [
    {
      "delivery_request_id": "drq_...",
      "external_reference": "ORDER-123",
      "status": "RECEIVED",
      "created_at": "2026-10-10T12:00:00Z"
    }
  ],
  "next_cursor": null
}
```

## 7. Evaluate / Quote

The implemented `GET /v1/delivery-requests/{delivery_request_id}` requires `delivery-requests.read` and returns only the authenticated customer's request summary. An unknown or foreign-customer ID returns 404, without exposing another tenant's data.

Current response:

```json
{
  "delivery_request_id": "drq_...",
  "external_reference": "ORDER-123",
  "status": "RECEIVED",
  "created_at": "2026-10-10T12:00:00Z"
}
```

Feasibility evaluation, quotes, and committed promises remain future capabilities. The example below describes a future response after those capabilities are implemented.

Future feasibility response example:

Possible result:

```json
{
  "delivery_request_id": "drq_...",
  "feasibility": {
    "result": "ACCEPTED"
  },
  "quote": {
    "quote_id": "qte_...",
    "currency": "IRT",
    "gross_fare": 35000,
    "expires_at": "2026-09-27T12:05:00Z"
  },
  "promise": {
    "committed_delivery_at": "2026-09-27T12:40:00Z"
  }
}
```

Alternative:

```json
{
  "feasibility": {
    "result": "ALTERNATIVE_PROMISE"
  },
  "promise": {
    "committed_delivery_at": "2026-09-27T12:53:00Z"
  }
}
```

Rejected:

```json
{
  "feasibility": {
    "result": "REJECTED",
    "reason_codes": ["NO_EFFECTIVE_CAPACITY"]
  }
}
```

## 8. Confirm / Create Mission

Explicit customer confirmation is implemented:

```
POST /v1/delivery-requests/{delivery_request_id}/confirm
```

Requires `delivery-requests.write` and an `Idempotency-Key`. The body supplies the Quote ID and current Promise version to confirm:

```json
{
  "quote_id": "qte_...",
  "promise_version": 1
}
```

Mono verifies that the authenticated customer's delivery request, unexpired Quote, and current Promise version match. A request can produce at most one Mission. The first successful confirmation creates a Mission in `PROMISED` state and writes `mission.created` to the transactional outbox in the same database transaction. Retrying the same key and body returns the original Mission; reusing the key with different content returns `409 IDEMPOTENCY_CONFLICT`. Confirming an already-confirmed request with another key returns `409 MISSION_ALREADY_CONFIRMED`. An expired Quote returns `422 QUOTE_EXPIRED`. Missing or foreign customer resources return not found without disclosing tenant data.

Response:

```json
{
  "mission_id": "mis_...",
  "delivery_request_id": "drq_...",
  "quote_id": "qte_...",
  "quote_version": 1,
  "promise_id": "prm_...",
  "promise_version": 1,
  "status": "PROMISED",
  "committed_delivery_at": "2026-09-27T12:40:00Z",
  "created_at": "2026-09-27T12:00:00Z"
}
```

Automatic Mission creation by customer contract is not implemented.

## 9. Get Mission

The current customer-scoped Mission summary is implemented:

```
GET /v1/missions/{mission_id}
```

Requires `delivery-requests.read`. The response includes Mission status, linked Quote and Promise versions, committed delivery time, and creation time. Unknown or foreign-customer Mission IDs return `404 MISSION_NOT_FOUND`. The response does not expose pickup/dropoff contact details or internal evidence. Courier, route, and exception details will be added with their operational capabilities.

### 9.1 Mission Timeline

`GET /v1/missions/{mission_id}/timeline` requires `delivery-requests.read` and returns the customer's Mission lifecycle event envelope in sequence order. The timeline reads only the tenant-owned Mission aggregate and allowlisted public Mission event types. It returns event ID, type, sequence, and occurrence time; internal event payloads and pickup/dropoff details are omitted. A missing or foreign-customer Mission returns `404 MISSION_NOT_FOUND`. The initial implementation exposes `mission.created`; later lifecycle events appear as those operations are implemented.

### 9.2 Mission History

GET /v1/missions?page_size=50&cursor=mis_... requires delivery-requests.read. It returns customer-scoped Mission summaries ordered by creation time descending and then Mission ID descending. The default page size is 50 and the maximum is 100. next_cursor is the last Mission ID on a non-final page; cursors must resolve to a Mission owned by the authenticated customer or the API returns 400 INVALID_CURSOR. The response reuses the safe Mission summary fields and excludes pickup/dropoff, contacts, and internal evidence.

## 10. Mission Status Model

External normalized statuses:

```
RECEIVED
VALIDATING
PLANNING
PROMISED
SEARCHING_COURIER
COURIER_ASSIGNED
EN_ROUTE_TO_PICKUP
ARRIVED_PICKUP
PICKED_UP
IN_TRANSIT
ARRIVED_DROPOFF
DELIVERED
COMPLETED
CANCELLED
FAILED
RETURNING
RETURNED
```

Internal states may be richer, but external semantics must remain stable.

## 11. Cancel Mission

Future capability; not implemented by the current request intake.

```
POST /v1/missions/{mission_id}/cancel
```

Request:

```json
{
  "reason_code": "CUSTOMER_CANCELLED",
  "external_reference": "cancel-123"
}
```

Response if allowed:

```json
{
  "mission_id": "mis_...",
  "status": "CANCELLED"
}
```

Response if cutoff passed:

```json
{
  "error": {
    "code": "CANCELLATION_NOT_ALLOWED",
    "current_status": "PICKED_UP",
    "reason_code": "HANDOFF_ALREADY_CONFIRMED"
  }
}
```

The actual cutoff is policy-driven.

## 12. Tracking

Future capability; not implemented by the current request intake.

```
GET /v1/missions/{mission_id}/tracking
```

Response:

```json
{
  "mission_id": "mis_...",
  "status": "IN_TRANSIT",
  "current_eta": "2026-09-27T12:38:00Z",
  "courier": {
    "public_id": "crr_public_...",
    "mode": "BIKE"
  },
  "position": {
    "latitude": 35.705,
    "longitude": 51.415,
    "recorded_at": "2026-09-27T12:28:00Z"
  }
}
```

Location visibility and retention are policy-controlled.

## 13. Mission Timeline

```
GET /v1/missions/{mission_id}/timeline
```

Returns customer-visible lifecycle events:

- accepted
- courier assigned
- arrived pickup
- handed to courier
- picked up
- in transit
- delivered
- failed
- cancelled

This endpoint is useful for reconciliation and support.

## 14. Handoff Evidence

```
GET /v1/missions/{mission_id}/handoffs
```

Response includes first-class custody events.

Example:

```json
{
  "handoffs": [
    {
      "handoff_event_id": "hnd_...",
      "type": "STORE_TO_COURIER",
      "occurred_at": "2026-09-27T12:15:00Z"
    },
    {
      "handoff_event_id": "hnd_...",
      "type": "COURIER_TO_RECIPIENT",
      "occurred_at": "2026-09-27T12:39:00Z"
    }
  ]
}
```

## 15. Delivery Evidence

```
GET /v1/missions/{mission_id}/delivery-evidence
```

May expose contract-approved fields such as:

- delivered_at
- customer_received_at
- confirmation_type
- recipient name confirmation status
- failed-delivery reason
- delivery location evidence

## 16. Webhook Subscription

```
POST /v1/webhook-endpoints
```

Example:

```json
{
  "url": "https://customer.example.com/mono/webhooks",
  "events": [
    "mission.accepted",
    "courier.assigned",
    "mission.handed_to_courier",
    "mission.delivered",
    "mission.failed"
  ]
}
```

Webhook URLs must be validated and secrets generated securely.

## 17. Webhook Security

Recommended:

- HTTPS only
- HMAC signature
- timestamp header
- event ID
- replay protection
- secret rotation

Example headers:

```
Mono-Event-Id
Mono-Event-Type
Mono-Timestamp
Mono-Signature
```

## 18. Webhook Delivery

Mono uses at-least-once delivery.

Customer must deduplicate by event_id.

Mono should expose:

```
GET /v1/webhook-deliveries
GET /v1/webhook-deliveries/{id}
POST /v1/webhook-deliveries/{id}/retry
```

The retry endpoint may be restricted to authorized customer/admin users.

## 19. Branches

```
GET /v1/branches
POST /v1/branches
PATCH /v1/branches/{branch_id}
```

The read endpoint is available to API keys with the `branches.read` scope.
Responses include `branch_id`, `name`, `address`, `latitude`, `longitude`,
`zone_id`, and `status`; internal customer IDs and operating-hours storage are
not exposed by this initial summary response.

Useful for Dashboard-based customers with multiple stores.

## 20. Customer Service Configuration

Read-only customer-facing view:

```
GET /v1/service-configurations
GET /v1/service-configurations/{id}
```

This may expose:

- enabled service
- coverage
- enabled modes
- commercial summary
- operational constraints

Raw internal policy definitions should not necessarily be exposed.

## 21. Pricing Policy Configuration

For customers contractually allowed to configure pricing:

```
GET /v1/pricing-configurations
PATCH /v1/pricing-configurations/{id}
```

Changes must be validated against contract/platform boundaries and versioned.

Example allowed changes:

- distance bands
- customer-defined fare cap
- branch-specific pricing
- subscription discount

A customer cannot modify platform safety or financial controls outside entitlement.

## 22. Dashboard Customers

Merchant Dashboard uses the same application/API use cases as external customers.

The Dashboard should not bypass core business rules.

```
Merchant Dashboard
  -> Mono API/Application Layer
  -> Shared Core
```

Therefore a store without its own app can:

- create request manually
- receive quote
- confirm Mission
- track courier
- cancel where allowed
- view history
- view invoices / balance

## 23. Bulk Upload

Future endpoint:

```
POST /v1/delivery-requests/bulk
```

Bulk requests must preserve per-item idempotency and validation result.

## 24. Error Model

Standard error:

```json
{
  "error": {
    "code": "OUTSIDE_COVERAGE",
    "message": "Delivery destination is outside current service coverage.",
    "reason_code": "ZONE_NOT_SUPPORTED",
    "request_id": "req_..."
  }
}
```

Machine-readable codes are mandatory.

## 25. Recommended Error Codes

- INVALID_REQUEST
- INVALID_LOCATION
- OUTSIDE_COVERAGE
- NO_ELIGIBLE_MODE
- NO_EFFECTIVE_CAPACITY
- SLA_NOT_FEASIBLE
- QUOTE_EXPIRED
- CANCELLATION_NOT_ALLOWED
- MISSION_ALREADY_COMPLETED
- IDEMPOTENCY_CONFLICT
- PAYMENT_REQUIRED
- CREDIT_LIMIT_EXCEEDED
- POLICY_CONFIGURATION_ERROR
- RATE_LIMITED
- UNAUTHORIZED
- FORBIDDEN

## 26. Pagination

List endpoints should use cursor pagination.

```
?limit=50&cursor=...
```

## 27. Time

All API timestamps must be ISO 8601 with timezone.

Internal storage should use UTC.

Customer-local display belongs to client/UI presentation.

## 28. Money

Avoid floating point.

Use integer minor/base units or explicit decimal representation.

Every money value must include currency.

## 29. Coordinates

Use decimal latitude / longitude.

Address text is for human operation.

Coordinates are the primary routing input when available.

## 30. API-to-Event Mapping

Typical workflow:

```
POST delivery-request
 -> delivery_request.received
 -> feasibility.accepted
 -> quote.created
 -> promise.committed
 -> mission.created
 -> courier.assigned
 -> mission.handed_to_courier
 -> mission.delivered
```

## 31. Ownership Boundary

Customer owns its business Order.

Mono owns logistics Mission.

Therefore:

```
customer_order_id != mono_mission_id
```

The customer's order reference is stored as `external_reference`.

## 32. Core Principle

> **One public B2B API surface, many customer configurations, one Mono logistics core.**


## 33. Mono Operations Mission Inspection

The following internal read routes require the existing MFA-gated Mono administrator policy:

```
GET /operations/v1/missions?page_size=50&status=PROMISED&query=HANA-ORDER&created_after=2026-10-01T00:00:00Z&created_before=2026-10-31T23:59:59Z&cursor=mis_...
GET /operations/v1/missions/{mission_id}
GET /operations/v1/missions/{mission_id}/timeline
```

Mission search uses keyset pagination ordered by `created_at DESC, mission_id DESC`. `page_size` defaults to 50 and must be between 1 and 100. Optional filters include canonical uppercase status, a 2–128 character case-insensitive prefix query over Mission ID, Delivery Request ID, or the stored external reference, and inclusive `created_after` / `created_before` timestamps. A cursor must identify a Mission matching every supplied filter.

Operations Mission summaries contain only Mission ID, customer ID, Delivery Request ID, status, committed delivery time, and creation time. Timeline responses expose the event ID, event type, sequence, and occurrence time; event payloads and personal/contact/location data are not returned. These endpoints are read-only.


Operations search, detail, and timeline reads create append-only accountability records containing the authenticated staff actor, action, Mission ID where applicable, result count, and timestamp. Search terms, external references, customer identifiers, and event payloads are not copied into this audit record. If the audit write fails, the read response fails closed.

### 33.1 Inspect Mission read audit

```
GET /operations/v1/mission-read-audit?page_size=50&mission_id=mis_...&actor_id=staff-1&created_after=2026-10-01T00:00:00Z&created_before=2026-10-31T23:59:59Z&cursor=mra_...
```

The endpoint is MFA-gated and returns audit ID, actor ID, action, Mission ID when applicable, result count, and occurrence time. Filters are optional and include Mission ID, staff actor, and inclusive occurrence-time bounds. Results use keyset pagination ordered by `occurred_at DESC, audit_id DESC`, with a page size of 1–100 (default 50). Cursors must identify a record that matches every active filter. Each audit-ledger inspection is itself recorded as `MISSION_AUDIT_INSPECTION`; its new entry is above the page cursor and will not shift subsequent pages.
