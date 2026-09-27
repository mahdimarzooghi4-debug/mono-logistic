# Mono Domain Model v1

## 1. Purpose

This document defines the first shared domain model for Mono B2B logistics.

The model must support:

- API-integrated customers,
- dashboard/manual customers,
- multiple industries,
- configurable policies,
- multiple vehicle/mode types,
- batching,
- routing,
- delivery promise,
- courier/provider execution,
- settlement,
- future asset ownership.

The model must remain shared across customers. Customer-specific behavior belongs in configuration and policy, not in customer-specific domain forks.

## 2. Core Aggregate Map

```
Customer
  -> Contract / Subscription
  -> Service Configuration
  -> Policies
  -> Delivery Request
  -> Mission
  -> Shipment
  -> Promise
  -> Route
  -> Assignment
  -> Courier / Provider / Vehicle
  -> Tracking
  -> Delivery
  -> Settlement
  -> Incident
```

## 3. Customer

Represents a B2B organization using Mono.

Key fields:

```
customer_id
legal_name
display_name
status
customer_type
default_channel
default_currency
created_at
```

Examples:

- Hana
- Local grocery store
- Pharmacy chain
- Retail business
- Enterprise platform

A Customer may have multiple branches, contracts, users and service configurations.

## 4. Branch

Represents an operational business location belonging to a Customer.

```
branch_id
customer_id
name
address
latitude
longitude
zone_id
operating_hours
status
```

A branch may be used as:

- pickup origin,
- billing entity,
- operational scope,
- branch-specific policy scope.

## 5. Contract / Subscription

Represents the commercial relationship between Mono and a Customer.

```
contract_id
customer_id
plan_type
billing_model
billing_cycle
credit_limit
status
effective_from
effective_to
```

Examples:

- Pay-as-you-go
- Subscription
- Enterprise contract
- Postpaid monthly

## 6. Service Configuration

Represents the Customer-specific configuration over a shared Mono service.

```
service_configuration_id
customer_id
service_type
status
enabled_modes
coverage_scope
policy_set_id
effective_from
effective_to
```

This is where Hana differs from another customer without creating a new product fork.

## 7. Policy

Represents a versioned rule or decision configuration.

```
policy_id
policy_type
scope
scope_id
version
status
enforcement_type
priority
effective_from
effective_to
definition
```

Policy definitions are evaluated by the Policy Engine.

## 8. Delivery Request

Represents the inbound business request before Mono commits to execution.

```
delivery_request_id
customer_id
branch_id
external_reference
requested_service
pickup
dropoff
shipment_summary
requested_time_window
requested_priority
status
created_at
```

A Delivery Request may result in:

- ACCEPTED
- ALTERNATIVE_PROMISE
- REJECTED

A Mission should only be created/committed when feasibility rules allow it.

## 9. Mission

Mission is the central Mono execution unit.

A Mission normally represents one customer delivery obligation.

```
mission_id
customer_id
delivery_request_id
external_reference
shipment_id
pickup_location_id
dropoff_location_id
promise_id
status
priority
created_at
completed_at
```

Mission is not Route.

One Route may contain multiple Missions.

## 10. Shipment

Represents the physical goods being transported.

```
shipment_id
mission_id
shipment_model
package_count
bag_count
estimated_weight
estimated_volume
load_class
declared_value
commodity_type
fragile
liquid
keep_upright
temperature_class
special_handling
```

The Shipment model must be extensible beyond FMCG.

## 11. Location

Represents normalized physical location data.

```
location_id
address_text
latitude
longitude
geocode_source
accuracy
zone_id
metadata
```

Locations may represent:

- branch,
- pickup,
- dropoff,
- courier current position,
- route stop.

## 12. Zone

Represents a geographic operating area.

```
zone_id
parent_zone_id
name
city
geometry_reference
status
capacity_policy_id
```

Zones may be hierarchical:

```
City
  -> Operating Zone
  -> Micro-zone
```

Zone is used for:

- eligibility,
- capacity,
- pricing,
- courier approval,
- operational monitoring.

## 13. Promise

Represents Mono's committed logistics obligation.

```
promise_id
mission_id
promise_type
committed_pickup_at
committed_delivery_at
promise_version
status
decision_id
created_at
```

Possible promise outcomes:

- committed
- alternative
- revised
- breached
- completed

Mono owns the delivery promise.

## 14. Feasibility Decision

Represents the auditable result of evaluating whether a Delivery Request can be served.

```
feasibility_decision_id
delivery_request_id
result
eligible_modes
eligible_candidate_count
candidate_routes
applied_policy_versions
reason_codes
evaluated_at
```

Possible results:

- ACCEPTED
- ALTERNATIVE_PROMISE
- REJECTED

## 15. Route

Represents the execution plan for one courier/provider resource.

```
route_id
executor_type
executor_id
vehicle_id
status
started_at
completed_at
route_version
```

A Route contains ordered Route Stops and may contain multiple Missions.

## 16. Route Stop

Represents an ordered pickup/dropoff/operational stop.

```
route_stop_id
route_id
sequence
stop_type
location_id
mission_id
planned_arrival_at
actual_arrival_at
planned_departure_at
actual_departure_at
status
```

Stop types may include:

- PICKUP
- DROPOFF
- RETURN
- HUB
- SERVICE

## 17. Mission Route Membership

Represents the relation between Mission and Route.

```
mission_route_id
mission_id
route_id
sequence_context
assigned_at
removed_at
status
```

This relation allows re-routing and re-dispatch while preserving history.

## 18. Courier

Represents an individual delivery operator in the Mono network.

```
courier_id
identity_status
trust_level
account_status
primary_mode
availability_status
wallet_account_id
created_at
```

Courier and Vehicle must remain separate concepts.

## 19. Vehicle

Represents an execution asset.

```
vehicle_id
vehicle_type
ownership_type
owner_id
payload_capacity
volume_capacity
temperature_capability
equipment
document_status
status
```

Examples:

- Bike
- Car
- Van
- Pickup
- Truck

Future ownership types may include:

- courier-owned,
- provider-owned,
- Mono-owned,
- lease-to-own.

## 20. Provider

Represents an external logistics execution organization.

```
provider_id
name
contract_status
coverage
supported_modes
service_status
settlement_profile
```

Mono may execute through:

- direct courier network,
- external provider,
- seller-as-courier where allowed.

## 21. Assignment

Represents a valid association between a Mission/Route and an executor.

```
assignment_id
mission_id
route_id
executor_type
executor_id
vehicle_id
status
offered_at
accepted_at
rejected_at
expired_at
```

Assignment must preserve offer/reject/timeout history.

## 22. Tracking Event

Represents operational movement/location evidence.

```
tracking_event_id
courier_id
route_id
mission_id
latitude
longitude
accuracy
heading
speed
event_time
source
```

Location tracking should be mission-aware and operationally justified.

## 23. Handoff Event

Represents physical transfer of custody at a boundary.

```
handoff_event_id
mission_id
handoff_type
from_actor
to_actor
occurred_at
location_id
evidence
source
```

Examples:

- STORE_TO_COURIER
- COURIER_TO_RECIPIENT

For Hana, STORE_TO_COURIER is a critical cancellation boundary.

## 24. Delivery Event

Represents final delivery outcome.

```
delivery_event_id
mission_id
delivery_status
recipient_confirmation_type
recipient_confirmation_value
delivered_at
location_id
exception_reason
```

Possible outcomes:

- DELIVERED
- FAILED
- CUSTOMER_UNAVAILABLE
- ADDRESS_ISSUE
- RETURN_REQUIRED

## 25. Incident

Represents logistics-side exception/issue evidence.

```
incident_id
mission_id
incident_type
severity
status
reported_by
reported_at
resolved_at
resolution
```

Customer-side commercial incident workflows may remain in the customer system, while Mono exposes logistics evidence and relevant logistics incidents.

## 26. Pricing Quote

Represents the commercial price decision before execution.

```
quote_id
customer_id
delivery_request_id
currency
gross_fare
discounts
surcharges
net_customer_charge
pricing_policy_versions
expires_at
status
```

Quote must remain auditable and versioned.

## 27. Ledger Account

Represents an accounting balance owner.

```
ledger_account_id
owner_type
owner_id
currency
account_type
status
```

Owners may include:

- Courier
- Customer
- Provider
- Mono

## 28. Ledger Entry

Represents an immutable financial posting.

```
ledger_entry_id
account_id
transaction_id
entry_type
amount
currency
reference_type
reference_id
created_at
```

Financial balances should be derived from ledger entries rather than mutable totals alone.

## 29. Settlement

Represents payable/receivable settlement processing.

```
settlement_id
counterparty_type
counterparty_id
period_start
period_end
gross_amount
deductions
tax
net_amount
status
created_at
settled_at
```

## 30. Withdrawal Request

Represents courier-initiated withdrawal.

```
withdrawal_request_id
courier_id
amount
status
requested_at
processed_at
destination_account
```

Customer-specific withdrawal limits belong in policy.

## 31. Decision Record

Represents an auditable policy/runtime decision.

```
decision_id
decision_type
subject_type
subject_id
input_snapshot
applied_policy_ids
applied_policy_versions
candidate_set
selected_result
reason_codes
evaluated_at
```

Important decision types:

- FEASIBILITY
- PROMISE
- MODE_SELECTION
- ASSIGNMENT
- BATCHING
- PRICING
- SLA_EXCEPTION
- SETTLEMENT_ADJUSTMENT

## 32. Asset Program Extension

Future Mono-owned asset financing can extend the domain with:

```
Asset
Asset Ownership
Lease Contract
Installment Schedule
Asset Assignment
Ownership Transfer
```

This should remain separate from Mission execution while integrating through Vehicle and Wallet/Ledger.

## 33. Core Invariants

1. Customer-specific behavior must not fork Mission/Route/Courier core models.
2. Mission and Route are separate.
3. Promise is explicit and versioned.
4. Handoff is a first-class auditable event.
5. Financial entries are immutable.
6. Important runtime decisions retain policy versions and reason codes.
7. Vehicle capability and courier identity are separate.
8. Customer Order and Mono Mission are separate bounded concepts.
9. Location is normalized and reusable across services.
10. Historical decisions must remain reproducible from stored evidence.
