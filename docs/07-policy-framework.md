# Mono Policy Framework v1

## 1. Purpose

Mono uses a shared B2B logistics core. Customer, industry, commercial and operational differences are expressed through configurable policies rather than product forks.

The framework is:

```
Policy Type
  -> Inputs
  -> Conditions
  -> Constraints
  -> Decision / Action
  -> Priority
  -> Version
```

Runtime model:

```
Request
  -> Load Customer Configuration
  -> Load Applicable Policies
  -> Evaluate Hard Policies
  -> Build Eligible Set
  -> Evaluate Optimization Policies
  -> Produce Runtime Decision
  -> Persist Decision Evidence
```

## 2. Policy Classes

### 2.1 Hard Policies

Hard Policies define eligibility and safety boundaries.

If a hard policy fails, the candidate or Mission is not allowed.

Examples:

- coverage / zone
- vehicle capacity
- commodity restriction
- temperature requirement
- trust requirement
- legal restriction
- maximum weight / volume
- required equipment
- customer-specific forbidden modes

### 2.2 Optimization Policies

Optimization Policies choose the best option among already-eligible candidates.

Examples:

- lowest ETA
- lowest detour
- highest route density
- best pickup synchronization
- courier earnings balance
- provider cost
- capacity balancing
- preferred execution mode

Optimization must never override a failed Hard Policy.

## 3. Policy Scope

A policy may apply at one or more scopes:

```
Platform
Service
Customer
Branch
Zone
Contract
Mission
Runtime
```

Precedence must be explicit.

Suggested precedence:

```
Platform Safety Rules
  > Service Rules
  > Customer / Contract Rules
  > Branch / Zone Overrides
  > Mission Runtime Inputs
```

No lower-level configuration may disable a non-overridable platform safety rule.

## 4. Standard Policy Schema

Each policy should contain at least:

```
policy_id
policy_type
scope
scope_id
version
status
priority
enforcement_type
effective_from
effective_to
inputs
conditions
constraints
decision
reason_code
created_at
updated_at
```

Recommended values:

```
status = DRAFT | ACTIVE | INACTIVE | RETIRED
enforcement_type = HARD | OPTIMIZATION
```

## 5. Inputs

Inputs may come from:

- Delivery Request
- Customer Configuration
- Shipment
- Package / Bag
- Address / Location
- Zone
- Courier
- Vehicle
- Provider
- Route
- Current Network State
- Current Time
- Contract / Subscription
- Pricing Context

Policy evaluation should use normalized internal facts instead of customer-specific field names.

## 6. Conditions

Conditions determine when a policy applies.

Examples:

```
weight_kg > 25
distance_km <= 5
temperature_class = CHILLED
customer_id = HANA
zone_id IN [...]
vehicle_type = BIKE
mission_priority = EXPRESS
```

Conditions should be declarative wherever possible.

## 7. Constraints

Constraints define what is allowed or forbidden.

Examples:

```
exclude_mode = BIKE
require_vehicle_type IN [VAN, PICKUP]
max_route_missions = 5
require_trust_level >= 3
require_temperature_capability = CHILLED
max_pickup_dwell_minutes = 3
```

## 8. Decision / Action

Possible actions include:

- ACCEPT
- REJECT
- ALTERNATIVE_PROMISE
- INCLUDE_CANDIDATE
- EXCLUDE_CANDIDATE
- REQUIRE_CAPABILITY
- APPLY_PRICE_RULE
- APPLY_SURCHARGE
- APPLY_DISCOUNT
- SET_MAX_WAIT
- REQUIRE_CONFIRMATION
- CREATE_EXCEPTION
- REQUIRE_MANUAL_REVIEW

## 9. Priority and Conflict Resolution

Policies may conflict.

Resolution must be deterministic.

Rules:

1. Hard Policies are evaluated before Optimization Policies.
2. Platform non-overridable rules always win.
3. More specific scope overrides less specific scope only when override is allowed.
4. Higher explicit priority wins within the same scope and policy family.
5. Equal-priority contradictions must fail closed and generate configuration error.
6. Every final decision must retain the policy IDs and versions that produced it.

## 10. Versioning

Policies must be versioned.

A Mission should retain the exact policy version used at decision time.

Example:

```
pricing_policy_version = 12
vehicle_policy_version = 4
sla_policy_version = 7
```

Changing a policy must not silently rewrite historical Mission decisions.

## 11. Effective Dating

Policies should support future activation and retirement:

```
effective_from
effective_to
```

This allows:

- planned tariff changes,
- contract renewal,
- temporary campaigns,
- zone changes,
- seasonal rules.

## 12. Decision Evidence

Every important runtime decision should be auditable.

Example:

```
decision_id
mission_id
decision_type
candidate_set
selected_candidate
applied_policy_ids
applied_policy_versions
input_snapshot
decision_result
reason_codes
evaluated_at
```

This is especially important for:

- Mission rejection,
- alternative promise,
- mode selection,
- courier selection,
- pricing,
- batching,
- cancellation cutoff,
- SLA exception,
- settlement adjustment.

## 13. Policy Presets

Industry templates are presets over the same policy framework.

Examples:

```
FMCG_PRESET
PHARMACY_PRESET
RETAIL_PRESET
RESTAURANT_PRESET
```

A preset may initialize policy values, but customers can receive contract-specific configuration within allowed boundaries.

## 14. Design Rule

Before adding special-case code, determine whether the requirement belongs to:

1. a new capability,
2. a new policy,
3. a new policy value,
4. a customer configuration,
5. a runtime input.

Only change the shared core when the underlying operating model truly changes.

## 15. Core Principle

> **Mono grows by expanding policies and capabilities, not by multiplying product forks.**
