# Mono Runtime Decision Architecture v1

## 1. Purpose

Mono's central differentiator is runtime decision-making.

A customer defines configuration and policies, but Mono decides how each actual Mission should be executed based on live network state.

Core model:

```
Customer Configuration
+ Policy Set
+ Shipment Facts
+ Location Facts
+ Live Network State
+ Active Routes
= Runtime Decision
```

## 2. Decision Pipeline

```
Request
  -> Normalize Facts
  -> Load Effective Configuration
  -> Load Applicable Policies
  -> Evaluate Hard Constraints
  -> Build Eligible Candidate Set
  -> Simulate Execution
  -> Evaluate Promise
  -> Evaluate Price
  -> Rank Eligible Options
  -> Commit Decision
  -> Persist Evidence
  -> Emit Events
```

## 3. Input Normalization

Customer-specific request fields must be converted into normalized Mono facts.

Examples:

```
address -> normalized location
bags -> shipment units
vehicle preference -> requested capability
customer SLA label -> normalized service window
```

Decision engines should not directly depend on Hana-specific or customer-specific field names.

## 4. Effective Configuration Resolution

Resolve configuration from:

```
Platform Defaults
  -> Service Defaults
  -> Industry Preset
  -> Customer Contract
  -> Customer Configuration
  -> Branch / Zone Override
  -> Mission Runtime Inputs
```

Only allowed override paths may change upstream values.

## 5. Policy Resolution

Policy service returns the effective policy set for the request context.

Example context:

```json
{
  "customer_id": "cus_...",
  "branch_id": "br_...",
  "service_type": "URBAN_DELIVERY",
  "pickup_zone_id": "zon_...",
  "dropoff_zone_id": "zon_...",
  "requested_at": "..."
}
```

Output:

- hard policies
- optimization policies
- commercial policies
- policy versions

## 6. Hard Constraint Stage

Hard constraints answer:

> What is allowed?

Examples:

- coverage valid?
- distance allowed?
- commodity allowed?
- temperature capability available?
- vehicle capacity sufficient?
- courier trust sufficient?
- route capacity valid?
- zone approved?
- service window feasible?

Failure removes candidate or rejects the Mission.

## 7. Candidate Generation

Build candidate set from:

- available couriers
- active routes
- eligible vehicles
- external providers
- allowed modes

Candidate generation should be broad enough to avoid missing good options, but bounded for runtime latency.

## 8. Route Opportunity Evaluation

For each candidate:

- estimate pickup arrival
- simulate route insertion
- calculate detour
- validate existing Mission promises
- validate capacity
- validate pickup dwell
- validate zone rules
- validate stop count

For batching, every insertion must create a new route simulation.

## 9. Promise Evaluation

Promise Engine asks:

```
Can Mono safely commit a delivery time?
```

Inputs:

- estimated ready time
- pickup travel
- pickup dwell
- route travel
- stop sequence
- current traffic/routing estimate
- existing Mission deadlines
- operational buffer
- live capacity

Output:

- ACCEPTED
- ALTERNATIVE_PROMISE
- REJECTED

## 10. Promise Safety Buffer

Committed delivery time should not equal theoretical best-case ETA.

Use configurable safety buffer based on:

- route uncertainty
- provider reliability
- zone condition
- time of day
- number of stops
- pickup variability

Exact formula can evolve.

## 11. Pricing Evaluation

Pricing Engine calculates commercial output using effective Pricing Policy.

Important:

> Execution choice and customer fare may be related, but they are separate decisions.

Example Hana:

- fare determined by distance band
- Bike vs Car does not change customer-facing price

Other customers may have:

- vehicle-based price
- weight-based price
- contract rate
- surcharge

## 12. Optimization Stage

After hard constraints pass, rank candidates.

Possible objectives:

- lowest delivery ETA
- minimum detour
- maximum batch density
- pickup synchronization
- best zone balance
- lower execution cost
- courier earning fairness
- preferred provider

Optimization scoring must never override hard constraints.

## 13. Decision Scoring

Recommended conceptual structure:

```
score =
  weighted_eta
  + weighted_detour
  + weighted_route_density
  + weighted_pickup_fit
  + weighted_capacity_balance
  + weighted_cost
```

Weights are policy/configuration values.

Do not hard-code business weights into core logic.

## 14. Decision Types

Mono should persist distinct decision records for:

- FEASIBILITY
- PROMISE
- PRICING
- MODE_SELECTION
- COURIER_SELECTION
- PROVIDER_SELECTION
- ROUTE_INSERTION
- REDISPATCH
- RETURN_EXECUTION
- SETTLEMENT_ADJUSTMENT

## 15. Commit Boundary

A runtime decision is not authoritative until committed.

Commit should atomically persist:

- selected result
- relevant aggregate state
- policy versions
- decision ID
- reason codes
- outbox events

## 16. Re-evaluation

Runtime decisions may need re-evaluation when:

- courier rejects
- courier goes offline
- route changes
- store ready time changes
- traffic changes materially
- SLA becomes at risk
- Mission cancels
- zone capacity changes
- provider becomes unavailable

Re-evaluation must create a new Decision Record, not overwrite history.

## 17. Re-dispatch

Re-dispatch pipeline:

```
Failure / Rejection
  -> Freeze current evidence
  -> Rebuild eligible set
  -> Re-simulate
  -> Re-check promise
  -> Select new executor
  -> Update route/assignment
  -> Emit redispatch event
```

If original promise is no longer feasible:

- mark SLA risk,
- apply customer policy,
- potentially generate revised/alternative outcome if contract permits.

## 18. Batching Decision

Adding Mission B to Route A requires:

1. load current Route version,
2. simulate insertion positions,
3. validate all existing Mission promises,
4. validate new Mission promise,
5. validate capacity,
6. validate pickup dwell,
7. validate zone constraints,
8. score valid insertion,
9. commit new Route version.

No "simple append" batching.

## 19. Mode Selection

Mode selection is capability-based.

Example:

```
Shipment Facts
  -> Hard Mode Constraints
  -> Eligible Modes
  -> Vehicle/Courier Availability
  -> Route Simulation
  -> Optimization
  -> Selected Mode
```

Adding Van/Pickup/Truck extends capabilities without changing the decision pattern.

## 20. Decision Reason Codes

Every rejection/exclusion should have machine-readable reason codes.

Examples:

- OUTSIDE_COVERAGE
- MAX_DISTANCE_EXCEEDED
- NO_ELIGIBLE_MODE
- VEHICLE_CAPACITY_EXCEEDED
- TEMPERATURE_CAPABILITY_MISSING
- TRUST_LEVEL_TOO_LOW
- NO_EFFECTIVE_CAPACITY
- SLA_NOT_FEASIBLE
- ROUTE_INSERTION_BREAKS_EXISTING_PROMISE
- PICKUP_DWELL_RISK
- PROVIDER_UNAVAILABLE

## 21. Explainability

Operations should be able to answer:

- Why was this Mission rejected?
- Why was Bike excluded?
- Why was Courier X selected?
- Why was a batch rejected?
- Which policy version applied?
- What live inputs were used?

This requires persisted Decision Records.

## 22. Runtime Latency

Decision engines are operational path components.

Target latency should be defined per decision class.

Examples:

- configuration lookup: very low latency
- feasibility: sub-second to low seconds
- assignment: low seconds
- route optimization: bounded by candidate set size

Actual SLOs should be measured before production.

## 23. Caching

Safe cache targets:

- active configuration
- active policy sets
- zone metadata
- provider capabilities
- static vehicle capability definitions

Live state such as capacity and availability requires fresh enough data.

## 24. Failure Strategy

If a dependency fails:

### Maps provider unavailable

- retry/failover provider if configured,
- use recent cache only when policy permits,
- otherwise reject/hold decision safely.

### Policy service unavailable

- use pinned active policy snapshot if valid,
- do not silently invent defaults.

### Capacity data stale

- reduce confidence,
- reject or use conservative promise policy.

## 25. Manual Override

Operations may override certain decisions only through explicit policy.

Manual override must include:

- actor
- reason
- old decision
- new decision
- timestamp
- affected Mission/Route
- audit record

Safety/legal Hard Policies must not be bypassed casually.

## 26. Decision Engine Components

Logical components:

```
Fact Normalizer
Configuration Resolver
Policy Resolver
Eligibility Engine
Candidate Generator
Route Simulator
Promise Engine
Pricing Engine
Scoring Engine
Decision Committer
Decision Audit Store
```

These may live in one deployable service initially.

## 27. Customer Independence

Runtime engine must not contain logic such as:

```
if customer == HANA:
   ...
```

Instead:

```
effective_policy_set = resolve(customer_context)
decision = evaluate(effective_policy_set, runtime_facts)
```

## 28. Industry Independence

Likewise, industry behavior should come from presets/policies.

Bad:

```
if industry == PHARMACY:
  require_refrigerated_vehicle()
```

Preferred:

```
temperature_policy.require_capability = CHILLED
```

## 29. Historical Reproducibility

A historical decision should be explainable from:

- input snapshot
- policy versions
- candidate set
- selected result
- route version
- map/routing response reference
- decision timestamp

Exact deterministic replay may not always be possible because external map/network state changes, but evidence must be sufficient to explain the decision.

## 30. Core Principle

> **Mono does not merely dispatch couriers; Mono evaluates constraints, simulates execution and commits the best valid logistics decision at runtime.**
