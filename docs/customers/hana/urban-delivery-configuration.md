# Hana Urban Delivery Configuration

## 1. Purpose

This document defines **Hana-specific configuration** for the shared **Mono Urban Delivery** service.

Hana does not receive a forked logistics product. Hana consumes the same Mono Urban Delivery service as future customers, with its own policies and configuration.

Architecture:

```
Mono Urban Delivery Core
  + Hana Customer Configuration
  + Runtime Network Context
  = Hana Runtime Delivery Decision
```

## 2. Ownership Boundary

### Hana owns

- customer order
- buyer and seller business rules
- customer cancellation eligibility
- buyer/seller payment logic
- customer refund policy
- customer-facing order incidents
- customer support workflow
- seller preparation workflow

### Mono owns

- logistics Mission
- courier eligibility
- courier assignment
- Route
- mode selection
- batching
- pickup execution
- handoff execution
- ETA / committed delivery promise
- tracking
- delivery execution
- logistics SLA
- courier earnings
- courier balance / withdrawal
- logistics-side exceptions

## 3. Integration Direction

Mono owns and publishes the logistics API contract.

```
Hana -> Mono API
Mono -> Hana Webhooks / Events
```

Hana is an API consumer of Mono Urban Delivery.

The API should remain reusable for future customers and must not be designed as a Hana-only private contract.

## 4. Phase-1 Service Scope

```
service_type = urban_delivery
max_distance_km = 5
enabled_modes = [walk, bike, car]
shipment_model = bag_based
```

Primary goods are supermarket orders, usually carried in bags.

## 5. Pricing Policy

Initial Hana pricing model:

| Distance | Delivery Fare |
|---|---:|
| 0–1 km | 20,000 toman |
| 1–2 km | 27,500 toman |
| 2–3 km | 35,000 toman |
| 3–4 km | 42,500 toman |
| 4–5 km | 50,000 toman |

Configuration:

```
pricing_model = distance_band
fare_cap = 50000
buyer_share = 50%
seller_share = 50%
mono_commission = 10%
vat_on_mono_commission = 10%
```

The selected execution mode does not change the customer-facing Hana tariff.

## 6. Promise Policy

Mono owns the delivery promise.

Mono must evaluate runtime feasibility before accepting and committing a delivery time.

Possible outcomes:

- ACCEPTED
- ALTERNATIVE_PROMISE
- REJECTED

A Hana distance band does not automatically imply a fixed delivery promise.

Promise must consider:

- Ready Time
- current Zone capacity
- eligible courier modes
- available couriers
- active Routes
- batching opportunities
- Route simulation
- all existing committed Mission deadlines

## 7. Pickup Policy

Hard rule:

> Pickup dwell from courier arrival at store until physical handoff must not exceed 3 minutes.

```
pickup_dwell_max_minutes = 3
```

Mono should schedule courier arrival as close as possible to the order Ready Time.

If pickup exceeds the allowed dwell due to store readiness:

```
exception = STORE_PREPARATION_DELAY
```

The delay must not be attributed to courier performance.

## 8. Batching Policy

```
batching_enabled = true
max_missions_per_route = 5
```

A new Mission may be added to a Route only when re-simulation proves that:

- all existing committed delivery promises remain valid,
- the new Mission promise remains valid,
- courier load remains valid,
- courier approved Zone rules remain valid,
- pickup timing remains feasible,
- store dwell hard rule can still be respected.

Mono should prefer dense batches such as:

- common pickup store,
- nearby pickup stores,
- same street,
- same block,
- same building cluster,
- same micro-zone.

This is especially important for Walk couriers.

## 9. Shipment Policy

Hana uses a bag-based shipment model.

Primary fields:

```
bag_count
estimated_total_weight
load_class
contains_liquid
contains_fragile
keep_upright
temperature_sensitive
```

Initial load classes:

- S: one light bag
- M: two to three bags or medium load
- L: more than three bags or heavy/bulky load

## 10. Mode Policy

Enabled modes:

- Walk
- Bike
- Car

Hana does not directly choose the execution mode.

Mono determines the mode at runtime using:

```
Distance
+ Bag Count
+ Weight
+ Load Class
+ Content Risk
+ Promise
+ Zone Capacity
+ Route Opportunity
```

## 11. Courier Policy

Courier must be:

- Mono approved,
- identity verified,
- financially verified,
- inside an approved operating Zone,
- eligible for the Mission risk level,
- active and available for assignment.

Mono may require:

- no-criminal-record certificate,
- approved electronic promissory note / guarantee,
- vehicle documentation for Bike / Car,
- performance eligibility.

## 12. Zone Policy

Courier operation is limited to Mono-approved Zones.

```
courier_selected_zones
-> Mono approval
-> approved_operating_zones
```

Hard rule:

> A courier must not receive or execute a Mission outside approved operating Zones.

Initial Hana planning rule:

```
base_registered_capacity = active_stores * 10
```

The courier pool is shared across stores inside the Zone.

Capacity dimensions:

- Registered Capacity
- Online Capacity
- Effective Capacity

## 13. Delivery Confirmation

Hana phase-1 delivery confirmation does not require OTP.

Primary confirmation:

```
recipient_name_confirmation
```

Operational flow:

```
ARRIVED_DROPOFF
-> courier asks recipient name
-> recipient identity is reasonably matched
-> handover
-> DELIVERED
```

The courier must not mark a Mission delivered when the recipient cannot be reasonably matched.

## 14. Cancellation Policy

Source of truth: Hana product policy.

Hana customer may cancel a delivery order until **physical handoff to the courier**.

Important:

- SELLER_CONFIRMED does not close cancellation.
- PREPARING does not close cancellation.
- READY_FOR_PICKUP does not close cancellation.
- Courier assignment alone does not close cancellation.

The effective cutoff is the valid logistics handoff event.

Suggested integration event:

```
HANDED_TO_COURIER
```

Mono must provide a reliable, idempotent and auditable handoff event with Mission identity and server-side timestamp.

Cancellation and handoff races must resolve to one authoritative result.

## 15. Post-Handoff Incident Policy

After valid courier handoff:

> Hana customer cancellation is no longer available.

Issues are handled through Hana's incident workflow.

Mono should provide logistics evidence needed by Hana, including where applicable:

- Mission identity
- handoff time
- courier assignment
- route timeline
- arrival events
- location evidence
- delivery event
- failed-delivery evidence
- exception reason codes

A reported issue must not be transformed into a fake pre-handoff cancellation.

## 16. Confirmed Non-delivery

If Hana's incident process confirms that the entire order was not delivered, refund behavior is owned by Hana.

Mono's responsibility is to provide auditable delivery execution evidence and logistics event history.

Mono should support states/events sufficient to distinguish:

- delivery attempted,
- delivery failed,
- delivery completed,
- non-delivery reported,
- logistics evidence under review.

## 17. Missing / Damaged Items

Missing or damaged grocery items are **Hana order-item incidents**, not automatic Mono Mission cancellation.

Hana currently owns:

- customer incident reporting,
- customer evidence,
- support review,
- refund decision,
- seller-side resolution.

Hana policy allows missing/damaged item reports within one hour of actual customer receipt.

Mono must provide the authoritative delivery receipt time required to anchor that Hana policy:

```
customer_received_at
```

This time must not be confused with:

- courier handoff at store,
- courier assignment,
- order ready time.

## 18. Financial Flow

Delivery money flow:

```
Buyer + Seller
  -> Hana
  -> Mono
  -> Courier
```

Courier earnings:

```
courier_net = mission_fare - mono_commission - vat_on_commission
```

With the current rules:

```
courier_net = mission_fare * 89%
```

After Mission completion:

```
courier_net -> courier_available_balance
```

Withdrawal rule:

```
max_withdrawals_per_day = 3
```

Funds may remain in the courier's Mono balance until withdrawal is requested.

## 19. API Data Expectations

Hana should send both human-readable address data and geographic coordinates whenever available.

Suggested destination/pickup data:

```
address_text
latitude
longitude
recipient_name
recipient_phone
address_details
```

Coordinates are the preferred input for Mono routing.

If coordinates are unavailable or invalid, Mono Maps Gateway may use geocoding as fallback.

## 20. Location & Routing

Hana uses the shared Mono Maps / Location architecture.

```
Courier GPS
-> Mono Location Service
-> Mono Maps Gateway
-> Routing / ETA Provider
-> Mono Promise & Route Engines
```

Mono Core must remain independent of any single map vendor.

## 21. Hana Configuration vs Mono Core

### Hana-configurable

- max distance
- enabled modes
- pricing bands
- fare cap
- payer split
- commission
- VAT policy
- pickup dwell
- batching limit
- shipment constraints
- courier trust requirements
- Zone requirements
- delivery confirmation method
- cancellation cutoff policy
- incident integration behavior
- settlement / withdrawal rules
- notification/event subscriptions

### Mono Core

The following must remain shared platform concepts and must not be forked per customer:

- Mission model
- Route model
- Courier model
- Location model
- Maps Gateway
- Feasibility Engine
- Promise Engine architecture
- Pricing Engine architecture
- Batching Engine architecture
- State Machine foundation
- Ledger foundation
- Audit trail
- security foundation
- API platform foundation

## 22. Core Principle

> **Mono customizes for Hana, but Mono does not fork for Hana.**

Hana is Customer Configuration #1 of Mono Urban Delivery.

Future customers should use the same service and API surface with their own configuration.
