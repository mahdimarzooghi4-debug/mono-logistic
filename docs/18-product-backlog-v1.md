# Mono Product Backlog v1

## 1. Goal

Convert the approved Business and Technical Architecture into buildable product work.

Backlog priority is based on the first operational target:

> Launch Mono B2B Urban Delivery with Hana as the first API customer, while preserving the shared multi-customer / policy-driven architecture.

## 2. Backlog Structure

```
Epic
  -> Capability
  -> User Story
  -> Acceptance Criteria
  -> Technical Tasks
```

## 3. Epic E1 — Customer, Contract & Configuration

### E1.1 Customer
- Create Customer
- Customer status
- Customer tenant context
- Customer external identifiers

### E1.2 Branch
- Create/update Branch
- Store pickup coordinates
- Operating hours
- Zone assignment

### E1.3 Contract / Subscription
- Contract type
- billing model
- credit limits
- enabled services
- access channels

### E1.4 Service Configuration
- customer-specific service configuration
- enabled modes
- coverage
- policy set binding
- effective dates

### Acceptance
- Hana can exist as Customer Configuration #1 without Hana-specific code.
- A second customer can be created with different configuration using the same core.

## 4. Epic E2 — Policy Engine

### E2.1 Policy CRUD
- create policy
- version policy
- activate/retire policy
- scope policy

### E2.2 Effective Policy Resolution
- platform defaults
- service defaults
- preset
- customer
- branch
- zone
- runtime context

### E2.3 Hard Policy Evaluation
- pass/fail
- reason codes
- candidate exclusion

### E2.4 Optimization Policy Inputs
- weights
- priorities
- scoring configuration

### Acceptance
- No decision path requires `if customer == HANA`.
- Every critical decision records policy IDs and versions.

## 5. Epic E3 — Delivery Request & Mission

### E3.1 Create Delivery Request
- external reference
- pickup
- dropoff
- shipment
- estimated ready time
- requested priority

### E3.2 Validation
- required fields
- valid coordinates
- customer entitlement
- duplicate/idempotent submission

### E3.3 Mission Creation
- Mission created only after accepted feasibility/confirmation
- external order reference preserved
- Mission lifecycle initialized

### E3.4 Mission Query
- status
- promise
- assignment
- timeline
- customer-scoped access

## 6. Epic E4 — Shipment & Capability Model

### E4.1 Shipment Facts
- bag count
- package count
- weight
- volume
- load class
- fragile
- liquid
- upright
- temperature class

### E4.2 Mode Capability Matrix
Initial:
- Walk
- Bike
- Car

Extensible:
- Van
- Pickup
- Truck

### Acceptance
- New vehicle mode can be added through capability/policy expansion without Mission core changes.

## 7. Epic E5 — Maps, Geocoding & Location

### E5.1 Maps Gateway
- provider abstraction
- geocode
- reverse geocode
- route
- ETA
- route matrix

### E5.2 Location Validation
- coordinate validation
- zone resolution
- address fallback geocoding

### E5.3 Courier Location Ingestion
- GPS event endpoint
- current position projection
- freshness
- mission-aware tracking

## 8. Epic E6 — Zone & Capacity

### E6.1 Zone Model
- city
- operating zone
- micro-zone

### E6.2 Courier Zone Approval
- preferred zones
- approved zones
- hard operating boundaries

### E6.3 Capacity
- registered
- online
- effective

### E6.4 Hana MVP Capacity Rule
```
registered_capacity_target = active_hana_stores * 10
```

## 9. Epic E7 — Feasibility & Promise

### E7.1 Feasibility
Evaluate:
- coverage
- distance
- eligible modes
- eligible couriers
- capacity
- active routes
- route insertion
- SLA feasibility

### E7.2 Results
- ACCEPTED
- ALTERNATIVE_PROMISE
- REJECTED

### E7.3 Promise
- committed_delivery_at
- version
- safety buffer
- reason evidence

### Acceptance
- Mono never commits a Mission before proving feasibility.

## 10. Epic E8 — Pricing

### E8.1 Pricing Engine
- distance bands
- flat
- base + per-km
- weight
- vehicle
- zone
- contract pricing
- min/max fare

### E8.2 Hana Pricing v1
- 0–1 km: 20,000 toman
- 1–2 km: 27,500
- 2–3 km: 35,000
- 3–4 km: 42,500
- 4–5 km: 50,000

### E8.3 Quote
- quote version
- expiration
- policy versions
- audit evidence

## 11. Epic E9 — Courier Onboarding & Trust

### E9.1 Courier Registration
- identity
- mobile
- national ID
- photo
- bank/IBAN
- mode
- zones

### E9.2 Documents
- vehicle docs
- trust documents
- background check
- electronic guarantee where applicable

### E9.3 Approval
```
REGISTERED
-> IDENTITY_VERIFIED
-> DOCUMENTS_SUBMITTED
-> TRUST_REQUIREMENTS_VERIFIED
-> MONO_APPROVED
-> ACTIVE
```

## 12. Epic E10 — Assignment

### E10.1 Candidate Generation
- available couriers
- active routes
- eligible modes
- approved zones

### E10.2 Offer Lifecycle
- offer
- accept
- reject
- timeout

### E10.3 Selection
- ETA
- route fit
- trust
- capacity
- batch opportunity
- zone balance

### E10.4 Re-dispatch
- rejection
- timeout
- courier unavailable
- route invalidation

## 13. Epic E11 — Routing & Batching

### E11.1 Route
- route creation
- route stops
- route versions

### E11.2 Route Simulation
- insertion positions
- detour
- ETA
- capacity
- promise validation

### E11.3 Batching
Hana MVP:
```
max_missions_per_route = 5
```

### Acceptance
- Adding a Mission reruns route simulation.
- No existing Mission promise can be broken.

## 14. Epic E12 — Pickup & Handoff

### E12.1 Arrival
- EN_ROUTE_TO_PICKUP
- ARRIVED_PICKUP

### E12.2 Pickup Dwell
Hana:
```
pickup_dwell_max = 3 minutes
```

### E12.3 Handoff
- first-class STORE_TO_COURIER event
- server timestamp
- auditable event ID
- cancellation race protection

## 15. Epic E13 — Delivery Execution

### E13.1 In Transit
- route progress
- ETA refresh
- SLA monitoring

### E13.2 Dropoff
- ARRIVED_DROPOFF
- recipient name confirmation
- DELIVERED

### E13.3 Failure
- customer unavailable
- address issue
- delivery failed
- return required

## 16. Epic E14 — Cancellation & Incident Integration

### E14.1 Policy-driven Cancellation
- customer cutoff
- Mission cancellation
- handoff race

### E14.2 Hana Rules
- cancellation allowed before physical handoff
- no cancellation after handoff
- post-handoff issues become Hana incidents

### E14.3 Evidence
- timeline
- location
- handoff
- delivery result

## 17. Epic E15 — Ledger, Settlement & Wallet

### E15.1 Ledger
- accounts
- transactions
- immutable entries

### E15.2 Courier Earnings
Hana:
```
Mono Commission = 10%
VAT on Commission = 10%
Courier Net = 89% of Mission Fare
```

### E15.3 Courier Balance
- pending
- available
- history

### E15.4 Withdrawal
- on demand
- max 3/day for current policy
- payout state
- reconciliation

## 18. Epic E16 — B2B API

### E16.1 Authentication
- customer credentials
- tenant context

### E16.2 Endpoints
- delivery requests
- confirm
- Mission status
- cancel
- tracking
- timeline
- handoffs

### E16.3 Idempotency
- Idempotency-Key
- duplicate-safe writes

## 19. Epic E17 — Webhooks & Events

### E17.1 Event Envelope
- event ID
- version
- correlation
- aggregate
- timestamp

### E17.2 Hana Webhooks
Initial:
- mission.accepted
- promise.committed
- courier.assigned
- mission.handed_to_courier
- mission.delivered
- mission.failed
- mission.cancelled

### E17.3 Delivery
- HMAC
- retry
- replay
- dead-letter
- delivery history

## 20. Epic E18 — Merchant Dashboard

For customers without their own app.

### MVP capabilities
- login
- branch selection
- manually create delivery
- quote
- confirm Mission
- track
- cancel where allowed
- Mission history
- basic billing view

Dashboard must use the same application/core rules as API customers.

## 21. Epic E19 — Operations Console

### MVP
- Mission search
- Mission timeline
- Route inspection
- courier state
- assignment
- zone capacity
- SLA alerts
- re-dispatch
- controlled exception handling
- audit trail

## 22. Epic E20 — Security

- tenant isolation
- RBAC
- secrets
- API auth
- webhook signing
- audit
- location privacy
- finance authorization
- security tests

## 23. Epic E21 — Observability

- structured logs
- metrics
- traces
- decision records
- dashboards
- alerts
- provider health
- GPS freshness
- event pipeline health

## 24. Epic E22 — Infrastructure & CI/CD

- development/staging/production
- PostgreSQL
- Redis
- queue
- object storage
- deployments
- migrations
- backup
- monitoring
- CI checks
- release approval

## 25. Deferred Epics

Not required for first Hana launch:

- Van/Pickup/Truck execution
- Pharmacy preset
- cold chain
- Insurance
- advanced liability
- external provider marketplace
- Asset lease-to-own
- Merchant subscription self-service
- advanced fraud ML
- customer self-service Policy Builder
- multi-region deployment

## 26. Backlog Rule

Every implementation item must trace back to:

```
Business Decision
-> Technical Capability
-> Epic
-> Story
-> Acceptance Criteria
-> Code
-> Test
```
