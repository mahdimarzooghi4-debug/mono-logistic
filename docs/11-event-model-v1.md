# Mono Event Model v1

## 1. Purpose

Mono uses domain events to connect bounded capabilities without tightly coupling state mutations.

Events must be:

- idempotent,
- versioned,
- timestamped,
- traceable,
- replay-safe where practical,
- attributable to an aggregate and source.

## 2. Standard Event Envelope

Every event should use a common envelope.

```json
{
  "event_id": "evt_...",
  "event_type": "mission.created",
  "event_version": 1,
  "occurred_at": "2026-09-27T12:00:00Z",
  "producer": "mission-service",
  "correlation_id": "corr_...",
  "causation_id": "evt_...",
  "customer_id": "cus_...",
  "aggregate_type": "mission",
  "aggregate_id": "mis_...",
  "sequence": 42,
  "data": {}
}
```

## 3. Event Naming

External event names:

```
<domain>.<action>
```

Examples:

- delivery_request.received
- feasibility.accepted
- promise.committed
- mission.created
- courier.assigned
- mission.handed_to_courier
- mission.delivered

Internal code may use uppercase constants, but public contracts should use stable lowercase names.

## 4. Delivery Request Events

- delivery_request.received
- delivery_request.validated
- delivery_request.rejected
- delivery_request.cancelled

## 5. Feasibility Events

- feasibility.started
- feasibility.accepted
- feasibility.alternative_offered
- feasibility.rejected

Payload should include:

- decision_id
- reason_codes
- eligible_modes
- evaluated_at

## 6. Pricing Events

- quote.created
- quote.updated
- quote.expired
- quote.accepted
- quote.rejected

Payload should include:

- quote_id
- gross_fare
- discounts
- surcharges
- net_customer_charge
- currency
- pricing_policy_versions

## 7. Promise Events

- promise.committed
- promise.revised
- promise.at_risk
- promise.breached
- promise.completed

Promise revisions must retain previous promise version.

## 8. Mission Events

- mission.created
- mission.validating
- mission.planning
- mission.promised
- mission.searching_courier
- mission.cancel_requested
- mission.cancelled
- mission.completed
- mission.failed

Mission state is owned by Mission Service.

## 9. Assignment Events

- assignment.offer_created
- assignment.offer_accepted
- assignment.offer_rejected
- assignment.offer_expired
- courier.assigned
- courier.unassigned
- mission.redispatch_started
- mission.redispatch_completed

## 10. Pickup / Handoff Events

- courier.en_route_to_pickup
- courier.arrived_pickup
- pickup.waiting_started
- pickup.delay_detected
- mission.handed_to_courier
- mission.picked_up

For customer systems such as Hana, `mission.handed_to_courier` is a critical contract event.

Suggested payload:

```json
{
  "mission_id": "mis_...",
  "handoff_event_id": "hnd_...",
  "courier_id": "crr_...",
  "route_id": "rte_...",
  "handoff_at": "2026-09-27T12:00:00Z",
  "pickup_location_id": "loc_...",
  "source": "mono"
}
```

## 11. In-Transit Events

- mission.in_transit
- route.updated
- route.reoptimized
- mission.sla_at_risk
- mission.exception_created

## 12. Delivery Events

- courier.arrived_dropoff
- delivery.confirmation_started
- mission.delivered
- mission.delivery_failed
- mission.return_required
- mission.return_started
- mission.return_completed

For successful delivery:

```json
{
  "mission_id": "mis_...",
  "delivery_event_id": "del_...",
  "delivered_at": "2026-09-27T12:30:00Z",
  "confirmation_type": "RECIPIENT_NAME",
  "customer_received_at": "2026-09-27T12:30:00Z"
}
```

## 13. Route Events

- route.created
- route.mission_added
- route.mission_removed
- route.resequenced
- route.started
- route.completed
- route.cancelled

A Route event must preserve route_version.

## 14. Location / Tracking Events

High-frequency raw GPS updates should not necessarily be published as customer-facing domain events.

Internal examples:

- courier.location_updated
- courier.zone_entered
- courier.zone_exited
- route.progress_updated

Customer-facing tracking should preferably expose current state/query APIs plus selected lifecycle events.

## 15. Zone / Capacity Events

- zone.capacity_warning
- zone.capacity_critical
- zone.capacity_recovered
- zone.closed
- zone.reopened

## 16. Courier Events

- courier.registered
- courier.approved
- courier.suspended
- courier.activated
- courier.availability_changed
- courier.trust_level_changed

## 17. Vehicle Events

- vehicle.registered
- vehicle.verified
- vehicle.suspended
- vehicle.capability_changed
- vehicle.assignment_changed

## 18. Policy / Configuration Events

- policy.created
- policy.activated
- policy.updated
- policy.retired
- customer_configuration.updated
- contract.updated

Policy events should include policy_id and policy_version.

## 19. Financial Events

- earning.calculated
- earning.posted
- commission.posted
- tax.posted
- settlement.created
- settlement.completed
- settlement.failed
- withdrawal.requested
- withdrawal.approved
- withdrawal.completed
- withdrawal.failed

Financial events must be tied to immutable ledger transaction IDs.

## 20. Incident / Exception Events

- incident.created
- incident.updated
- incident.resolved
- exception.store_preparation_delay
- exception.customer_unavailable
- exception.address_issue
- exception.courier_no_show
- exception.delivery_failed

## 21. External Webhook Events

Initial external webhook catalog:

- mission.accepted
- mission.rejected
- promise.committed
- courier.assigned
- courier.arrived_pickup
- mission.handed_to_courier
- mission.picked_up
- mission.in_transit
- mission.sla_at_risk
- mission.delivered
- mission.failed
- mission.cancelled
- mission.return_required
- mission.return_completed

Customers subscribe according to Notification / Integration Policy.

## 22. Delivery Semantics

External webhook delivery should be at-least-once.

Therefore customers must deduplicate using `event_id`.

Mono should:

- retry transient failures,
- use exponential backoff,
- sign webhook requests,
- expose delivery attempt history,
- support dead-letter handling,
- allow replay for authorized operators.

## 23. Ordering

Ordering is guaranteed only per aggregate where technically supported.

Consumers must not assume global event ordering.

Use:

- aggregate_id
- sequence
- occurred_at

to validate event progression.

## 24. Idempotency

All event consumers must persist processed `event_id` or equivalent idempotency evidence before applying duplicate-sensitive side effects.

## 25. Schema Versioning

Breaking payload changes require a new `event_version`.

Old versions should remain supported for a defined compatibility window.

## 26. Transactional Outbox

Recommended pattern:

```
Domain State Transaction
  -> Outbox Record
  -> Event Publisher
  -> Broker / Webhook Dispatcher
```

This prevents state commit without event publication intent.

## 27. Correlation

All events in the same customer request/workflow should carry a common `correlation_id`.

Example:

```
Delivery Request
 -> Feasibility
 -> Quote
 -> Promise
 -> Mission
 -> Assignment
 -> Delivery
```

## 28. Audit Rule

Business-critical events must retain:

- actor/source
- aggregate
- event time
- server receive time
- policy versions where relevant
- correlation ID
- reason codes
- previous/new state when appropriate

## 29. Core Principle

> **Events describe facts that happened; commands request actions. Do not model commands as events.**
