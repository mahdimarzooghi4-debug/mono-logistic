# Mono Service Boundaries v1

## 1. Purpose

This document defines logical service boundaries for Mono.

These are **bounded capabilities**, not a requirement to deploy each one as an independent microservice on day one.

For MVP, several boundaries may live inside one modular application while preserving clean ownership and contracts.

## 2. Architectural Principle

> **Design as bounded domains first; deploy as microservices only when scale, reliability or team boundaries justify it.**

Recommended initial approach:

```
Modular Monolith
  + Event-driven boundaries
  + Clear module ownership
  + Independent data contracts
  -> selective service extraction later
```

## 3. API Gateway / B2B Access

Responsibilities:

- customer authentication,
- API keys / OAuth credentials,
- rate limiting,
- idempotency keys,
- request validation,
- version routing,
- API observability,
- customer context propagation.

Consumers:

- Hana,
- enterprise platforms,
- Merchant Dashboard,
- future Merchant App,
- Bulk Upload / assisted channels.

This boundary does not own Mission business logic.

## 4. Customer & Contract Service

Owns:

- Customer
- Branch
- Contract
- Subscription
- Billing profile
- Service entitlement
- Access channel entitlement

Key outputs:

- active customer context,
- contract terms,
- credit/plan eligibility,
- enabled services.

## 5. Configuration & Policy Service

Owns:

- Service Configuration
- Policy definitions
- Policy versions
- Policy scopes
- presets
- effective dates
- override rules

Provides:

```
getEffectivePolicies(context)
evaluatePolicySet(context)
```

The Policy Engine should return:

- result,
- reason codes,
- applied policy IDs,
- versions.

## 6. Delivery Request / Mission Service

Owns:

- Delivery Request
- Mission
- Mission lifecycle
- customer external references
- Mission state transitions
- Mission-level idempotency

Responsibilities:

- create request,
- initiate feasibility,
- commit accepted Mission,
- cancel Mission when allowed,
- expose Mission state.

Mission Service is the central operational source of truth.

## 7. Feasibility & Promise Service

Owns:

- feasibility evaluation,
- promise decision,
- committed delivery time,
- alternative promise,
- rejection reason.

Consumes:

- Customer Configuration
- Policies
- Shipment facts
- Location / routing
- Zone capacity
- Courier/provider availability
- active Routes

Outputs:

- ACCEPTED
- ALTERNATIVE_PROMISE
- REJECTED

Mono must not create a committed delivery promise without this boundary.

## 8. Pricing Service

Owns:

- Pricing Quote
- fare calculation
- discounts
- surcharges
- minimum/maximum fare
- contract rate application
- pricing policy versions

Pricing is shared infrastructure.

Customer-specific pricing is configuration, not customer-specific code.

## 9. Shipment Service

Owns:

- Shipment
- package/bag facts
- weight
- volume
- commodity classification
- handling requirements
- temperature requirement
- declared value

Provides normalized facts to Policy, Feasibility and Assignment.

## 10. Courier Network Service

Owns:

- Courier
- courier status
- KYC status
- trust profile
- approved zones
- availability
- operational performance profile

Does not own financial ledger.

## 11. Fleet / Vehicle Service

Owns:

- Vehicle
- vehicle type
- capabilities
- payload/volume limits
- document status
- ownership type
- courier/provider association

Supports future Mono-owned and lease-to-own assets.

## 12. Provider Service

Owns:

- external Provider
- contracts
- provider capabilities
- coverage
- service health
- supported modes
- provider-level performance

Used when execution is not directly assigned to an individual Mono courier.

## 13. Assignment Service

Owns:

- candidate generation,
- courier/provider eligibility,
- offers,
- acceptance,
- rejection,
- timeout,
- re-dispatch,
- executor selection.

Consumes:

- Policy Engine
- Route state
- Courier availability
- Vehicle capabilities
- Zone state
- Promise constraints

Outputs Assignment events.

## 14. Routing & Optimization Service

Owns:

- Route
- Route Stop
- route simulation
- batching decisions
- stop sequencing
- detour calculation
- route versioning

Consumes Mono Maps Gateway for travel-time and distance data.

Important:

- Mission != Route
- One Route may contain multiple Missions.

## 15. Maps Gateway

Owns provider abstraction for:

- geocoding,
- reverse geocoding,
- route calculation,
- ETA,
- route matrix,
- map provider integration.

It must prevent direct vendor coupling in core business services.

## 16. Location & Tracking Service

Owns:

- courier location stream,
- normalized GPS events,
- current courier position,
- route progress,
- mission-aware tracking lifecycle.

Consumers:

- Assignment
- Routing
- Promise
- SLA Monitoring
- Operations
- Customer tracking views

## 17. Zone & Capacity Service

Owns:

- Zone
- Micro-zone
- coverage state
- registered capacity
- online capacity
- effective capacity
- zone health

Used by:

- Promise
- Assignment
- Routing
- Operations.

## 18. Handoff & Delivery Service

Owns:

- pickup arrival
- pickup dwell
- physical handoff event
- in-transit delivery execution
- dropoff arrival
- recipient confirmation
- delivery result
- failed delivery reason

Critical events:

```
ARRIVED_PICKUP
HANDED_TO_COURIER
PICKED_UP
ARRIVED_DROPOFF
DELIVERED
DELIVERY_FAILED
```

Handoff must be first-class and auditable.

## 19. SLA & Exception Service

Owns:

- SLA timers
- SLA-at-risk detection
- store preparation delay
- courier delay
- route risk
- delivery exceptions
- operational escalation state

Examples:

- STORE_PREPARATION_DELAY
- SLA_AT_RISK
- COURIER_NO_SHOW
- CUSTOMER_UNAVAILABLE
- ADDRESS_ISSUE

## 20. Incident Evidence Service

Owns Mono-side evidence for incidents:

- route timeline,
- handoff evidence,
- delivery evidence,
- location evidence,
- exception history.

It may expose evidence to customer systems such as Hana while customer-specific refund/support workflows remain outside Mono unless contractually required.

## 21. Ledger Service

Owns:

- Ledger Account
- Ledger Entry
- immutable financial postings
- balances derived from entries
- transaction references
- financial audit.

No other service should directly mutate financial balances.

## 22. Settlement Service

Owns:

- courier payable
- provider payable
- Mono commission
- VAT/tax deductions
- settlement cycles
- adjustments
- withdrawal eligibility
- settlement status.

Consumes Ledger Service for postings.

## 23. Withdrawal Service

Owns:

- withdrawal request
- withdrawal daily limit enforcement
- bank destination validation
- payout lifecycle
- payout reconciliation.

Can be deployed with Settlement for MVP.

## 24. Notification Service

Owns:

- Webhook delivery
- push
- SMS
- email
- retry
- dead-letter handling
- delivery receipts

Consumes domain events and customer Notification Policy.

## 25. Integration Event Service

Owns:

- external event contracts,
- webhook event versioning,
- retry,
- customer subscription mapping,
- event delivery state.

Example events:

```
mission.accepted
mission.rejected
promise.committed
courier.assigned
courier.arrived_pickup
mission.handed_to_courier
mission.picked_up
mission.in_transit
mission.sla_at_risk
mission.delivered
mission.failed
mission.cancelled
```

## 26. Operations Service / Console Backend

Supports Mono operations staff.

Capabilities:

- Mission search
- Route inspection
- courier state
- zone capacity
- SLA alerts
- re-dispatch
- exception handling
- controlled manual overrides
- audit trail

All manual intervention must create an auditable action record.

## 27. Audit Service

Owns immutable operational audit metadata:

- who changed configuration,
- who performed manual override,
- policy version used,
- state transition source,
- financial adjustment reason,
- security-sensitive actions.

Audit may be implemented initially as shared infrastructure rather than a separate deployable service.

## 28. Identity & Authorization

Owns:

- Mono staff identity,
- Customer users,
- Merchant users,
- courier authentication,
- roles,
- permissions,
- scoped access.

This is separate from business Customer identity.

## 29. Observability

Cross-cutting capability for:

- logs
- metrics
- traces
- event lag
- SLA metrics
- API health
- provider health
- queue health
- decision latency

Observability is mandatory for Promise and live logistics operations.

## 30. Suggested MVP Deployment

Do not begin with dozens of microservices.

Suggested first deployment:

```
1. mono-api
   - API Gateway
   - Customer/Contract
   - Mission
   - Shipment
   - Pricing
   - Policy

2. mono-operations
   - Courier
   - Vehicle
   - Assignment
   - Route
   - Delivery
   - SLA/Exceptions

3. mono-location
   - Location Tracking
   - Maps Gateway
   - Zone/Capacity

4. mono-finance
   - Ledger
   - Settlement
   - Withdrawal

5. mono-events
   - Webhooks
   - Notifications
   - Integration Events
```

These may still begin in a single repository and common deployment if operational simplicity is more valuable.

## 31. Event-driven Boundaries

Services should communicate important state changes through domain events.

Examples:

```
DELIVERY_REQUEST_RECEIVED
FEASIBILITY_ACCEPTED
FEASIBILITY_REJECTED
PROMISE_COMMITTED
MISSION_CREATED
COURIER_ASSIGNED
COURIER_ARRIVED_PICKUP
HANDED_TO_COURIER
MISSION_PICKED_UP
MISSION_IN_TRANSIT
SLA_AT_RISK
MISSION_DELIVERED
MISSION_FAILED
MISSION_COMPLETED
EARNING_POSTED
SETTLEMENT_CREATED
WITHDRAWAL_REQUESTED
```

Events must be:

- idempotent,
- versioned,
- timestamped,
- traceable to aggregate IDs.

## 32. Data Ownership Rule

Each bounded service owns its primary data.

Examples:

- Mission Service owns Mission state.
- Routing owns Route.
- Courier Network owns Courier.
- Ledger owns Ledger Entries.
- Pricing owns Quote.
- Policy owns Policy versions.

Other services consume through contracts/events, not direct cross-module mutation.

## 33. Transaction Boundary Rule

Cross-service workflows should avoid distributed transactions where possible.

Recommended pattern:

```
Local Transaction
  -> Outbox Event
  -> Async Consumers
  -> Idempotent Processing
```

Critical synchronous decisions such as feasibility may call read/query interfaces, but state mutation ownership must remain clear.

## 34. Core Technical Principle

> **Keep the domain modular from day one, but keep deployment complexity proportional to actual scale.**
