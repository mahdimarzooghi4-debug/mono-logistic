# Mono MVP Scope v1

## 1. MVP Objective

Launch a production-capable version of Mono that can execute Hana urban deliveries end-to-end while preserving the shared B2B platform architecture.

MVP is not a demo.

It must support real:

- customer integration,
- courier operations,
- promises,
- routing,
- delivery,
- settlement,
- tracking,
- audit.

## 2. First Customer

```
Customer = Hana
Channel = API
Service = Urban Delivery
Configuration = Hana-specific
```

## 3. MVP Modes

- Walk
- Bike
- Car

Architecture remains extensible for:

- Van
- Pickup
- Truck

but those are not required for first release.

## 4. MVP Geographic Scope

Initial operating zones only.

Requirements:

- approved zones
- micro-zones
- max Hana distance 5 km
- courier zone enforcement
- effective capacity tracking

## 5. MVP Request Flow

```
Hana Order Confirmed
-> Delivery Request
-> Validation
-> Feasibility
-> Quote
-> Promise
-> Mission
-> Courier Assignment
-> Pickup
-> Handoff
-> Delivery
-> Completion
-> Earning
-> Settlement
```

## 6. MVP Promise

Must support:

- accepted
- alternative promise
- rejected
- committed delivery time
- SLA at risk

No static distance-only promise.

## 7. MVP Maps

Required:

- geocoding fallback
- reverse geocoding
- route
- ETA
- route matrix
- live courier location
- provider abstraction

## 8. MVP Pricing

Hana distance bands:

| Distance | Fare |
|---|---:|
| 0–1 km | 20,000 toman |
| 1–2 km | 27,500 toman |
| 2–3 km | 35,000 toman |
| 3–4 km | 42,500 toman |
| 4–5 km | 50,000 toman |

Customer buyer/seller split remains a Hana concern unless specific settlement integration requires Mono visibility.

## 9. MVP Batching

- enabled
- up to 5 Missions per Route
- route simulation required
- all committed promises preserved
- load/capacity preserved
- pickup timing preserved

## 10. MVP Pickup

Hard Hana rule:

```
pickup_dwell <= 3 minutes
```

Need:

- courier arrival timestamp
- handoff timestamp
- store delay reason

## 11. MVP Cancellation Boundary

Hana customer may cancel until physical STORE_TO_COURIER handoff.

Mono must expose authoritative handoff event.

After handoff:

- no Hana cancellation
- issues use incident flow
- Mono supplies evidence

## 12. MVP Delivery Confirmation

No OTP required for Hana.

Use:

- recipient name confirmation
- delivery timestamp
- delivery location evidence
- Mission/courier reference

## 13. MVP Courier Network

Required:

- registration
- KYC
- trust/document status
- approved zones
- Walk/Bike/Car
- availability
- assignment
- current position
- performance basics

## 14. MVP Assignment

Required:

- candidate generation
- hard eligibility
- offer
- accept/reject/timeout
- re-dispatch
- route-aware selection

## 15. MVP Finance

Required:

- immutable ledger
- earning calculation
- Mono commission
- VAT on commission
- courier available balance
- max 3 withdrawals/day
- payout state
- reconciliation

## 16. MVP API

Required:

- authentication
- create delivery request
- get evaluation
- confirm
- Mission query
- cancellation
- tracking
- timeline
- handoffs
- delivery evidence

## 17. MVP Webhooks

Required:

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

## 18. MVP Operations Console

Required:

- Mission search
- Mission status
- Route
- courier
- zone capacity
- SLA risk
- re-dispatch
- manual exception action
- audit

## 19. MVP Security

Required:

- tenant isolation
- RBAC
- HTTPS
- secrets management
- audit
- webhook signatures
- API rate limits
- finance authorization
- privacy-safe location access

## 20. MVP Observability

Required:

- API health
- Mission metrics
- promise metrics
- pickup dwell
- assignment latency
- GPS freshness
- route simulation latency
- webhook failures
- ledger/settlement failures
- alerts

## 21. MVP Non-Goals

Not required for first production release:

- every industry preset
- Van/Pickup/Truck
- customer self-service policy builder
- AI dispatch
- advanced ML fraud
- Mono-owned vehicle program
- lease-to-own
- international/multi-region
- sea/air/rail
- full consumer app
- custom core for any customer

## 22. MVP Exit Criteria

MVP is ready for production only when:

1. Hana can create a real delivery request through API.
2. Mono returns a valid fare and promise.
3. Mission can be assigned to an eligible courier.
4. Courier can execute pickup and delivery.
5. Handoff is auditable.
6. Live tracking is available.
7. Cancellation race is handled safely.
8. Batching cannot violate existing promises.
9. Courier earning posts correctly.
10. Withdrawal rules work.
11. Hana receives signed webhooks.
12. Operations can intervene safely.
13. Tenant isolation tests pass.
14. Critical observability/alerts are live.
15. Backup and rollback have been tested.

## 23. MVP Principle

> **Build the smallest production system that proves Mono's operating model, not the smallest collection of screens.**
