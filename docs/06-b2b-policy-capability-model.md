# Unified B2B Policy & Capability Model

## 1. Decision

Mono B2B should use one shared logistics operating model across industries.

Industry must not create a separate logistics core by default.

The shared model is:

```
Customer
  -> Delivery Request
  -> Feasibility
  -> Promise
  -> Mission
  -> Route
  -> Delivery
  -> Settlement
```

Differences between customers, use cases and industries are expressed through:

- capabilities,
- policies,
- customer configuration,
- runtime context.

## 2. Core Principle

> **One B2B Logistics Model + More Policies + Customer Configuration + Runtime Decision**

Mono should extend behavior by adding policies and capabilities rather than by forking the product.

## 3. Industry as Preset, Not Core

Industry may be used as a convenient preset or template.

Examples:

- FMCG Profile
- Pharmacy Profile
- Retail Profile
- Restaurant Profile
- Automotive Parts Profile

A profile may preconfigure policies, but it does not create a separate core engine.

Example:

```
FMCG Profile
  -> bag-oriented shipment defaults
  -> Walk / Bike / Car
  -> batching enabled
```

```
Pharmacy Profile
  -> temperature-sensitive support
  -> stronger trust requirements
  -> chain-of-custody requirements
```

```
Retail Profile
  -> larger packages
  -> Car / Van / Pickup
  -> return-oriented policies
```

## 4. Runtime Execution

The runtime engine should decide execution based on actual Mission requirements, not just customer industry.

Relevant inputs include:

- distance,
- weight,
- volume,
- package count,
- load class,
- fragile flag,
- liquid flag,
- temperature sensitivity,
- security level,
- SLA,
- return requirement,
- approved zones,
- courier capability,
- vehicle capability,
- route opportunity,
- current network capacity.

## 5. Mode Extensibility

Modes are capabilities, not separate products.

Current and future examples:

```
Walk
Bike
Car
Van
Pickup
Truck
```

Adding Van or Pickup should extend the Mode / Vehicle Policy and capability matrix without requiring a new logistics core.

## 6. Policy Families

Mono B2B should support configurable policy families such as:

- Shipment Policy
- Mode Policy
- Vehicle Policy
- SLA Policy
- Pricing Policy
- Batching Policy
- Zone Policy
- Trust Policy
- Security Policy
- Temperature Policy
- Return Policy
- Cancellation Policy
- Incident Policy
- Delivery Confirmation Policy
- Settlement Policy
- Notification Policy
- Integration Policy

Additional policies may be introduced as new operational requirements appear.

## 7. Customer Configuration

Each customer receives a configuration over the shared core.

Example:

```
Mono B2B Core
  + Customer Configuration
  + Optional Industry Preset
  + Runtime Context
  = Runtime Decision
```

Examples:

```
Hana
  -> API channel
  -> Enterprise contract
  -> FMCG preset
  -> Hana-specific policies
```

```
Local Store
  -> Merchant Dashboard
  -> Subscription Plan
  -> Standard merchant configuration
  -> Optional FMCG preset
```

## 8. Product Boundary

The shared core should remain stable across customers:

- Mission
- Route
- Courier
- Vehicle
- Location
- Feasibility
- Promise
- Assignment
- Routing
- Batching
- Delivery
- Ledger
- Audit
- Security foundation

Policies control behavior around the core; they do not redefine these concepts per customer.

## 9. Design Rule

When a new business request appears, first ask:

1. Is this a new capability?
2. Is this a new policy?
3. Is this a new customer configuration?
4. Is this a new runtime input?

Only create a new service/core when the operating model is fundamentally different and cannot be represented safely through those layers.

## 10. Strategic Outcome

This approach allows Mono to serve multiple B2B industries while preserving:

- one technical core,
- one API platform,
- one Mission model,
- one Route model,
- one operational model,
- one runtime decision architecture.

The platform grows mainly by expanding its policy and capability catalog.
