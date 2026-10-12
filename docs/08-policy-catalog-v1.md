# Mono Policy Catalog v1

This catalog defines the first policy families supported by the Mono B2B logistics model.

The catalog is intentionally broad. MVP implementations may activate only the policies required by current customers.

## 1. Coverage & Zone Policy

Controls:

- supported cities
- supported zones
- approved courier zones
- pickup coverage
- dropoff coverage
- cross-zone permission
- maximum service distance
- temporary zone closure

Typical enforcement: HARD

## 2. Shipment Policy

Controls:

- bag / package model
- package count
- bag count
- weight
- volume
- dimensions
- load class
- fragile flag
- liquid flag
- keep-upright requirement
- stackability
- special handling markers

Typical enforcement: HARD + descriptive inputs

## 3. Commodity Policy

Controls commodity classes such as:

- general goods
- grocery / FMCG
- fragile
- liquid
- perishable
- chilled
- frozen
- pharmaceutical
- high-value
- document
- restricted / prohibited

Typical enforcement: HARD

## 4. Mode Policy

Controls allowed execution modes:

- Walk
- Bike
- Car
- Van
- Pickup
- Truck

May define:

- max distance
- max weight
- max volume
- allowed commodity classes
- allowed zones
- customer-specific mode exclusions

Typical enforcement: HARD

## 5. Vehicle Policy

Controls:

- vehicle type
- payload
- cargo volume
- dimensions
- refrigeration
- box/body type
- required equipment
- insurance requirement
- vehicle age / compliance where applicable

Typical enforcement: HARD

## 6. Courier Eligibility Policy

Controls:

- Mono approval
- identity status
- document validity
- trust level
- performance requirements
- incident history
- vehicle eligibility
- zone eligibility
- account status

Typical enforcement: HARD

## 7. Provider Eligibility Policy

For external providers, controls:

- service coverage
- supported modes
- capacity
- service status
- SLA eligibility
- contract status
- price eligibility
- failure/cancellation limits

Typical enforcement: HARD

## 8. Security Policy

Controls:

- minimum trust level
- chain-of-custody
- delivery proof type
- restricted courier groups
- high-value handling
- location evidence
- manual review requirements

Typical enforcement: HARD

## 9. Temperature Policy

Controls:

- ambient
- chilled
- frozen
- allowed exposure time
- vehicle capability
- packaging requirement
- temperature evidence where required

Typical enforcement: HARD

## 10. Service Window Policy

Controls:

- on-demand
- scheduled
- same-day
- exact delivery slot
- pickup window
- delivery window
- earliest pickup
- latest delivery

Typical enforcement: HARD

## 11. Ready-Time & Pickup Policy

Controls:

- estimated ready time
- dispatch timing
- courier arrival target
- pickup dwell allowance
- store delay classification
- pickup grace period

Example Hana rule:

```
pickup_dwell_max_minutes = 3
```

Typical enforcement: HARD

## 12. SLA & Promise Policy

Controls:

- promise creation
- committed delivery time
- alternative promise
- SLA at-risk threshold
- priority classes
- max elapsed time
- rejection conditions

Typical enforcement: HARD

## 13. Batching Policy

Controls:

- batching enabled
- max Missions per Route
- max total load
- pickup clustering
- dropoff clustering
- max detour
- route density preference
- priority Mission exclusions

Typical enforcement: HARD + OPTIMIZATION

## 14. Multi-stop Policy

Controls:

- multiple pickups
- multiple dropoffs
- stop ordering
- mandatory sequence
- stop count limit
- stop dwell time

Typical enforcement: HARD + OPTIMIZATION

## 15. Route Optimization Policy

Controls optimization objectives such as:

- ETA
- detour
- total distance
- route density
- pickup synchronization
- capacity use
- courier productivity

Typical enforcement: OPTIMIZATION

## 16. Assignment Policy

Controls candidate ranking using:

- pickup ETA
- delivery feasibility
- route fit
- courier trust
- performance
- batching opportunity
- earnings balance
- zone balance
- provider preference

Typical enforcement: OPTIMIZATION after eligibility

## 17. Pricing Policy

Controls price construction using one or more rules:

- flat fare
- distance bands
- base + per-km
- weight
- volume
- vehicle type
- zone
- service window
- waiting
- handling
- priority
- contract rate
- subscription discount
- surcharge
- minimum fare
- maximum fare

Important:

> Customers may configure pricing within contractually allowed boundaries; they do not replace the Mono Pricing Engine.

Typical enforcement: COMMERCIAL

## 18. Subscription & Contract Policy

Controls:

- subscription plan
- monthly fee
- included usage
- discounted Mission pricing
- API entitlement
- dashboard entitlement
- branch count
- user seats
- support tier
- reporting entitlement
- billing cycle

Typical enforcement: COMMERCIAL

## 19. Credit & Payment Policy

Controls:

- prepaid
- wallet
- postpaid
- credit limit
- invoice terms
- auto-charge
- deposit requirement
- payment failure behavior

Typical enforcement: HARD + COMMERCIAL

## 20. Waiting Policy

Controls:

- free waiting time
- waiting charge
- maximum waiting
- pickup waiting
- dropoff waiting
- waiting exception handling

Typical enforcement: HARD + COMMERCIAL

## 21. Cancellation Policy

Controls:

- cancellation cutoff
- allowed actors
- pre-pickup cancellation
- post-pickup behavior
- cancellation fee
- mission cancellation propagation
- cancellation / handoff race handling

Typical enforcement: HARD

## 22. Return / Reverse Logistics Policy

Controls:

- return allowed
- return trigger
- return destination
- same-courier return
- new Mission requirement
- return pricing
- return SLA
- return proof

Typical enforcement: HARD + COMMERCIAL

## 23. Incident Policy

Controls:

- incident types
- evidence requirements
- assignment of operational responsibility
- escalation
- manual review
- settlement hold
- exception reason codes
- recovery workflow

Typical enforcement: HARD / WORKFLOW

## 24. Liability Policy

Controls:

- liability boundaries
- loss
- damage
- delay
- capped liability
- excluded commodity classes
- required evidence
- escalation path

This policy requires contract/legal validation per jurisdiction.

Typical enforcement: CONTRACTUAL

## 25. Insurance Policy

Controls:

- insurance required
- declared value
- coverage threshold
- insurer/provider
- premium
- claim trigger
- evidence requirements

Typical enforcement: HARD + COMMERCIAL

## 26. Delivery Confirmation Policy

Possible methods:

- recipient name
- OTP
- signature
- photo
- customer app confirmation
- scan / barcode
- combination

Typical enforcement: HARD

## 27. Notification Policy

Controls subscriptions to events such as:

- Mission accepted
- courier assigned
- pickup started
- picked up
- SLA at risk
- delivered
- failed
- cancelled
- returned

Channels may include:

- webhook
- push
- SMS
- email
- dashboard

Typical enforcement: CONFIGURATION

## 28. Integration Policy

Controls:

- API access
- webhook access
- dashboard-only operation
- rate limits
- authentication method
- idempotency requirements
- event subscriptions
- allowed callback endpoints

Typical enforcement: HARD / CONFIGURATION

## 29. Data & Privacy Policy

Controls:

- data minimization
- location retention
- customer visibility
- courier visibility
- operational access
- evidence retention
- deletion / archival rules

Must be aligned with applicable privacy and contractual requirements.

Typical enforcement: HARD

## 30. Fraud & Abuse Policy

Controls:

- GPS spoofing indicators
- duplicate accounts
- suspicious delivery confirmations
- wallet abuse
- repeated cancellation abuse
- collusion indicators
- manual review thresholds

Typical enforcement: HARD + RISK

## 31. Settlement Policy

Controls:

- earning calculation
- commission
- tax
- adjustments
- penalties
- available balance timing
- pending balance
- withdrawal limit
- settlement cycle

Example current courier rule:

```
max_withdrawals_per_day = 3
```

Typical enforcement: FINANCIAL

## 32. Access Channel Policy

Controls how a B2B customer uses Mono:

- API
- Merchant Dashboard
- Merchant App
- Bulk Upload
- Assisted Operations

This allows customers with or without their own application to use the same Mono logistics core.

Typical enforcement: CONFIGURATION

## 33. Branch Policy

Controls:

- enabled branches
- branch pickup locations
- branch operating hours
- branch-specific pricing
- branch-specific service windows
- branch-specific zone coverage

Typical enforcement: CONFIGURATION

## 34. Manual Operations Policy

Controls when Mono operators may:

- override assignment
- re-dispatch
- adjust address
- cancel Mission
- force exception
- correct settlement
- approve special execution

All manual actions must be auditable.

Typical enforcement: HARD / GOVERNANCE

## 35. Policy Composition Example

Example request:

```
Customer = Retailer A
Weight = 32 kg
Distance = 6 km
Commodity = General
Requested SLA = Same Day
```

Evaluation:

```
Coverage Policy -> eligible
Shipment Policy -> eligible
Mode Policy -> Bike excluded
Vehicle Policy -> Van / Pickup eligible
Courier Policy -> build eligible courier set
SLA Policy -> feasible
Pricing Policy -> contract fare + vehicle rule
Assignment Policy -> select best eligible courier
```

The result is produced without creating a retailer-specific logistics core.

## 36. MVP Activation

For current Mono / Hana phase, the first activated policy families should include:

- Coverage & Zone
- Shipment
- Mode
- Courier Eligibility
- Ready-Time & Pickup
- SLA & Promise
- Batching
- Route Optimization
- Assignment
- Pricing
- Cancellation
- Incident Integration
- Delivery Confirmation
- Settlement
- Integration
- Location / Privacy

Other policy families can remain defined but inactive until a real customer or service requires them.

## 37. Core Principle

> **Policy breadth creates market breadth while the operating model stays unified.**
