# Mono Epics Roadmap v1

## Phase A — Platform Foundation

### Epic A1 — Repository & Engineering Foundation
- project structure
- environments
- CI
- migrations
- lint/type/test
- secrets
- observability baseline

### Epic A2 — Identity / Tenant / Customer
- authentication
- customer
- branch
- tenant isolation
- RBAC

### Epic A3 — Configuration / Policy
- policy schema
- policy versioning
- effective config resolution
- Hana configuration

## Phase B — Logistics Core

### Epic B1 — Delivery Request / Mission
- request
- validation
- idempotency
- Mission lifecycle

### Epic B2 — Shipment / Location / Zone
- shipment facts
- coordinates
- zone
- maps abstraction

### Epic B3 — Feasibility / Promise / Pricing
- hard constraints
- routing inputs
- quote
- promise
- decision record

## Phase C — Courier Execution

### Epic C1 — Courier / Vehicle
- onboarding
- verification
- zone approval
- availability

### Epic C2 — Assignment
- candidate set
- offer
- accept
- timeout
- re-dispatch

### Epic C3 — Route / Batching
- Route
- stops
- route simulation
- max 5 Hana Missions
- versioning

### Epic C4 — Pickup / Handoff / Delivery
- arrival
- dwell
- handoff
- in transit
- recipient confirmation
- delivered/failed

## Phase D — Integration & Operations

### Epic D1 — Hana API
- delivery request
- quote
- confirm
- Mission query
- cancellation
- tracking

### Epic D2 — Webhooks
- signed events
- retries
- replay
- delivery history

### Epic D3 — Operations Console
- Mission
- Route
- courier
- zone
- SLA
- re-dispatch
- audit

## Phase E — Finance

### Epic E1 — Ledger
- accounts
- entries
- transactions
- balances

### Epic E2 — Courier Earnings
- commission
- VAT
- available balance

### Epic E3 — Withdrawal / Reconciliation
- max 3/day
- payout lifecycle
- reconciliation

## Phase F — Production Readiness

### Epic F1 — Security Hardening
- tenant tests
- secrets
- auth
- webhook verification
- finance permissions

### Epic F2 — Reliability
- retry
- outbox
- inbox
- queues
- provider failure

### Epic F3 — Observability
- dashboards
- alerts
- SLO baseline
- correlation

### Epic F4 — Release
- staging E2E
- load test
- backup restore
- rollback
- release approval

## Post-MVP Expansion

### Merchant B2B
- Dashboard
- subscription plans
- manual Mission creation
- branches
- billing

### Mode Expansion
- Van
- Pickup
- Truck

### Policy Expansion
- temperature
- insurance
- advanced returns
- commodity/security
- special handling

### Asset Platform
- Mono-owned assets
- lease-to-own
- installment collection
- ownership transfer

## Roadmap Rule

No phase may bypass required safety/correctness gates because a later phase contains "hardening".

Security, audit and testability must exist from the first implementation; later phases deepen them.
