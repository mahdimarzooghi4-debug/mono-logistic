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

## 5. Idempotency

All create/command endpoints should accept:

```
Idempotency-Key
```

Mono must return the original result for safe retries where possible.

Critical examples:

- create delivery request
- accept quote
- cancel mission
- create withdrawal
- manual operational commands

## 6. Create Delivery Request

```
POST /v1/delivery-requests
```

Example request:

```json
{
  "external_reference": "ORDER-123",
  "branch_id": "br_...",
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
    "keep_upright": true
  },
  "requested_service": "URBAN_DELIVERY",
  "requested_priority": "STANDARD",
  "estimated_ready_at": "2026-09-27T12:10:00Z"
}
```

Possible response:

```json
{
  "delivery_request_id": "drq_...",
  "status": "EVALUATING"
}
```

## 7. Evaluate / Quote

Mono may evaluate automatically after request creation.

Query:

```
GET /v1/delivery-requests/{delivery_request_id}
```

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

Depending on contract, Mission may be auto-created or require customer confirmation.

Explicit confirmation:

```
POST /v1/delivery-requests/{delivery_request_id}/confirm
```

Request:

```json
{
  "quote_id": "qte_...",
  "promise_version": 1
}
```

Response:

```json
{
  "mission_id": "mis_...",
  "status": "PROMISED",
  "committed_delivery_at": "2026-09-27T12:40:00Z"
}
```

## 9. Get Mission

```
GET /v1/missions/{mission_id}
```

Response may include:

- current status
- pickup/dropoff
- promise
- selected mode
- courier public status
- route progress summary
- current ETA
- exception state

Sensitive courier data must not be exposed unless contractually required.

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
