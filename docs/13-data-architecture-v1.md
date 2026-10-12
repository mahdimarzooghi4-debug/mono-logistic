# Mono Data Architecture v1

## 1. Purpose

This document defines the first data architecture for Mono B2B logistics.

Goals:

- clear ownership,
- auditable state,
- safe financial records,
- reproducible runtime decisions,
- scalable operational data,
- customer isolation,
- support for API and Dashboard channels.

## 2. Data Architecture Principles

1. Each bounded domain owns its primary data.
2. Cross-domain mutation through direct database writes is prohibited.
3. Financial records are immutable.
4. Operational history must be auditable.
5. Runtime decisions must retain policy versions and input evidence.
6. Customer data must be logically isolated by `customer_id`.
7. High-frequency telemetry must not overload transactional tables.
8. Historical events should remain queryable without mutating source facts.

## 3. Initial Storage Strategy

Recommended MVP:

- PostgreSQL for transactional/domain data
- Redis for short-lived cache, locks and live state
- Object storage for evidence/files
- Event/outbox tables for reliable integration
- Time-series capable storage or partitioned PostgreSQL tables for location telemetry

Do not introduce unnecessary database technologies before operational need exists.

## 4. Domain Data Ownership

### Customer / Contract

Owns:

- customers
- branches
- contracts
- subscriptions
- service configurations

### Policy

Owns:

- policies
- policy versions
- policy scopes
- presets
- activation windows

### Mission

Owns:

- delivery requests
- missions
- mission state transitions
- customer external references

### Shipment

Owns:

- shipments
- shipment attributes
- package/bag facts
- commodity attributes

### Promise / Feasibility

Owns:

- feasibility decisions
- promises
- promise versions
- reason codes

### Pricing

Owns:

- quotes
- quote components
- pricing decision evidence

### Courier / Fleet

Owns:

- couriers
- courier trust
- courier zones
- vehicles
- vehicle capabilities
- provider relationships

### Routing

Owns:

- routes
- route stops
- mission-route memberships
- route versions

### Delivery

Owns:

- handoff events
- delivery events
- pickup dwell evidence
- delivery confirmation

### Finance

Owns:

- ledger accounts
- ledger entries
- transactions
- settlements
- withdrawals

## 5. Transactional Core Tables

Suggested table families:

```
customers
branches
contracts
service_configurations
policies
policy_versions
delivery_requests
missions
shipments
locations
zones
promises
feasibility_decisions
quotes
couriers
vehicles
providers
assignments
routes
route_stops
mission_route_memberships
handoff_events
delivery_events
incidents
decision_records
ledger_accounts
ledger_entries
settlements
withdrawal_requests
outbox_events
audit_records
```

Exact physical schema can evolve, but ownership boundaries should remain stable.

## 6. Multi-tenant Data Isolation

Every customer-owned or customer-scoped record should carry `customer_id` directly or through a guaranteed parent relation.

Recommended safeguards:

- tenant-aware repository/query layer
- row-level security where appropriate
- customer context propagated from authentication
- no unscoped list/search endpoints
- tenant ID in audit records
- tenant ID in event envelope

## 7. IDs

Use globally unique opaque IDs.

Recommended external form:

```
cus_...
br_...
drq_...
mis_...
rte_...
crr_...
veh_...
qte_...
prm_...
evt_...
```

Do not expose sequential database IDs externally.

## 8. State History

Important aggregates should keep current state plus append-only history.

Example:

```
missions
mission_state_history
```

State history fields:

```
history_id
mission_id
from_state
to_state
reason_code
actor_type
actor_id
occurred_at
correlation_id
```

Current state is optimized for reads; history remains auditable.

## 9. Policy Version Persistence

A decision must reference exact policy versions used at evaluation time.

Example:

```
decision_policy_links
- decision_id
- policy_id
- policy_version
```

Do not resolve old decisions against current policy values.

## 10. Decision Snapshots

Important decisions should persist normalized facts used at decision time.

Example:

```json
{
  "distance_km": 3.2,
  "bag_count": 2,
  "weight_kg": 8,
  "zone_capacity": 17,
  "eligible_modes": ["BIKE", "CAR"]
}
```

Snapshots allow:

- audit,
- support,
- reproducibility,
- policy debugging,
- analytics.

## 11. Location Telemetry

High-frequency GPS data is operational telemetry, not normal transactional data.

Suggested model:

```
courier_location_events
- courier_id
- route_id
- mission_id
- latitude
- longitude
- accuracy
- heading
- speed
- recorded_at
- received_at
```

Recommendations:

- partition by time,
- retention policy,
- indexes on courier/time and route/time,
- separate current-position cache,
- avoid updating one giant courier row on every GPS tick.

## 12. Current Position

Use a fast current-state projection:

```
courier_current_position
```

or Redis.

This is derived state.

The authoritative history remains location event data.

## 13. Route Versioning

Every material route re-sequencing should increment:

```
route_version
```

Preserve:

- previous stop order,
- new stop order,
- reason,
- decision ID,
- change timestamp.

## 14. Handoff Data

Handoff must be append-only evidence.

Never overwrite handoff history.

Suggested fields:

```
handoff_event_id
mission_id
handoff_type
from_actor_type
from_actor_id
to_actor_type
to_actor_id
occurred_at
location_id
evidence_ref
source
correlation_id
```

## 15. Delivery Evidence

Delivery evidence may include:

- recipient confirmation
- timestamp
- location
- photo/signature reference where policy requires
- exception reason

Large binary evidence belongs in object storage, not directly in relational rows.

## 16. Object Storage

Use object storage for:

- courier documents
- vehicle documents
- delivery evidence
- incident attachments
- signed documents
- future asset contracts

Database stores metadata and immutable object reference.

## 17. Ledger Architecture

Ledger must be double-entry or equivalent balanced accounting logic.

Core rules:

- append-only postings
- no editing posted ledger entries
- corrections use compensating entries
- balances are derived
- every posting has a business reference
- every posting belongs to a transaction

Suggested entities:

```
ledger_transactions
ledger_entries
ledger_accounts
```

## 18. Money Representation

Never use floating-point numbers.

Use integer minor/base units or fixed decimal.

Every amount includes currency.

## 19. Outbox

Each domain that emits events should write an outbox record in the same transaction as state mutation.

```
outbox_events
- event_id
- aggregate_type
- aggregate_id
- event_type
- payload
- created_at
- published_at
- retry_count
```

Publisher processes pending records asynchronously.

## 20. Inbox / Consumer Idempotency

Event consumers should persist processed event IDs.

```
consumer_inbox
- consumer_name
- event_id
- processed_at
```

This protects duplicate side effects.

## 21. Distributed Workflow State

Long-running workflows should use explicit workflow/saga state rather than hidden chained callbacks.

Examples:

- delivery request to committed Mission
- re-dispatch
- return
- settlement
- withdrawal

Workflow state should include:

- current step
- correlation ID
- retry count
- last error
- next retry time

## 22. Audit Data

Audit records should capture:

- actor
- action
- resource
- before/after where safe
- reason
- timestamp
- request ID
- customer ID
- IP/device metadata where appropriate

Audit is distinct from domain events.

## 23. Soft Delete

Avoid soft-delete everywhere by default.

Use:

- status transitions for business entities,
- archive timestamps where necessary,
- immutable retention for financial/audit/evidence records.

Hard deletion must follow retention/privacy policy.

## 24. Retention

Retention should be policy-driven by data class.

Example classes:

- financial
- legal/audit
- customer operational
- courier GPS
- temporary cache
- evidence attachments

Exact periods require legal/commercial validation.

## 25. Read Models

Operational UI should use purpose-built read models.

Examples:

```
mission_operations_view
zone_capacity_view
courier_availability_view
customer_mission_history_view
settlement_summary_view
```

Do not force every dashboard screen to perform large joins over transactional tables.

## 26. Search

Search use cases:

- mission by external reference
- mission by phone
- courier by ID/mobile
- customer/branch
- route
- incident

MVP may use PostgreSQL indexes.

Introduce dedicated search infrastructure only when needed.

## 27. Caching

Good cache candidates:

- effective customer configuration
- active policy sets
- zone metadata
- current courier position
- provider health
- route matrix results for short TTL

Never treat cache as source of truth.

## 28. Locking / Concurrency

Critical concurrency cases:

- cancellation vs handoff
- offer acceptance by multiple couriers
- route mutation
- withdrawal limits
- ledger posting
- policy version activation

Use:

- optimistic concurrency / version columns,
- transactional row locks where required,
- unique constraints,
- idempotency keys.

## 29. Backup & Recovery

Transactional and financial databases require:

- point-in-time recovery,
- tested backups,
- recovery procedures,
- monitoring of backup success.

RPO/RTO should be defined before production launch.

## 30. Data Migration

All schema changes must be:

- version-controlled,
- backward-compatible where possible,
- reversible or safely forward-fixable,
- tested against production-like data.

## 31. Privacy Boundary

Tracking and personal data access must be role- and purpose-limited.

Examples:

- customer sees only contracted tracking fields,
- courier sees only Mission data required for execution,
- operations access is audited,
- GPS history is retained only per policy.

## 32. Analytics

Operational analytics should eventually consume events/read replicas instead of querying transactional production tables heavily.

Initial KPI areas:

- on-time delivery
- pickup dwell
- acceptance
- route density
- batch size
- courier earnings/hour
- zone health
- Mission margin
- cancellation
- incident rate

## 33. Core Principle

> **Transactional truth, operational telemetry, financial ledger and analytical data are different data classes and must not be mixed casually.**
