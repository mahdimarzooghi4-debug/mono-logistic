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
- Sprint 018 adds Mono-administrator onboarding for new customers in PENDING status and audited activation to ACTIVE.
- Sprint 024 adds administrator-only customer status and append-only onboarding/activation audit inspection.
- Sprint 026 adds an MFA-gated Mono operations customer directory with keyset pagination and summary-only fields.
- Sprint 027 adds `PENDING` and `ACTIVE` status filters to the admin customer directory without changing customer lifecycle rules.
- Sprint 028 adds case-insensitive prefix search by customer ID, legal name, or display name to the admin customer directory.
- Sprint 029 adds Mono-admin-managed, namespaced external customer identifiers with uniqueness, retirement, and immutable audit history.
- Create Customer
- Customer status
- Customer tenant context
- Customer external identifiers

### E1.2 Branch
- Sprint 019 adds Mono-administrator onboarding for branches owned by active customers, with paired coordinate validation and branch audit history.
- Sprint 020 adds audited operations updates for existing active branches; omitted optional fields are cleared by the full-replacement contract.
- Sprint 021 adds an admin-only, customer-scoped read view of branch summaries for Mono operations.
- Sprint 030 adds an MFA-gated branch detail view and paginated, customer-scoped branch audit history.
- Sprint 031 adds Mono-admin-managed, customer-scoped external branch identifiers with uniqueness, retirement, and immutable audit history.
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
- Sprint 010 establishes versioned, effective-dated customer/service configuration lookup with tenant scoping and fail-closed ambiguity handling
- Sprint 011 adds internal Draft creation and audited activation, requiring trusted Mono administrator context; no public endpoint or Hana configuration is added
- Sprint 012 adds an internal read-only view of configuration versions and append-only audit history for Mono administrators
- Sprint 013 adds a provider-neutral OIDC JWT validation boundary for Mono operations, strict issuer/audience/signature/lifetime validation, and MFA-gated administrator claims; the identity provider and endpoint remain unconfigured
- Sprint 014 verifies the operations JWT boundary end-to-end with a local test issuer and signed tokens; no external provider is contacted
- Sprint 015 exposes read-only customer service configuration versions and audit history through Mono administrator-only operations routes.
- Sprint 016 exposes internal Mono-administrator routes to create Draft customer service configurations and activate them with the existing overlap checks and append-only audit trail.
- Sprint 017 adds audited retirement of active customer service configurations through the Mono administrator boundary.
- Sprint 018 adds an internal Mono-operations API to create a customer in PENDING status and activate it with an append-only audit trail.
- Sprint 019 adds an admin-only route to register an active branch for an already-active customer with append-only audit history.
- Sprint 020 adds full-replacement branch updates scoped to the owning customer, retaining coordinate checks and recording append-only audit history.
- enabled modes
- coverage
- policy set binding
- effective dates

### Acceptance
- Hana can exist as Customer Configuration #1 without Hana-specific code.
- A second customer can be created with different configuration using the same core.

## 4. Epic E2 — Policy Engine

### E2.1 Policy CRUD
- Sprint 032 adds MFA-gated policy draft creation, immutable version history, paginated listing, and append-only audit inspection.
- Sprint 033 adds MFA-gated activation and retirement with transactional status changes and append-only audit history; policy resolution and evaluation remain separate work.
- create policy
- version policy
- activate/retire policy
- scope policy

### E2.2 Effective Policy Resolution
- Sprint 034 adds context-aware resolution of active, effective policy versions, ordered by the documented scope tiers and priority; it does not evaluate policies or alter request decisions.
- platform defaults
- service defaults
- preset
- customer
- branch
- zone
- runtime context

### E2.3 Hard Policy Evaluation
- Sprint 035 adds an MFA-gated HARD policy evaluation preview with pass/fail, reason codes, fail-closed unsupported definitions, and candidate exclusion; it is not connected to delivery intake.
- pass/fail
- reason codes
- candidate exclusion

### E2.4 Optimization Policy Inputs
- Sprint 036 validates configurable WEIGHTED_SUM inputs (supported criteria, non-negative weights, positive priorities) without calculating scores or ranking candidates.
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
- Sprint 007 implements authenticated request intake, active customer branch validation, and customer-scoped idempotency; feasibility, quote, promise, and Mission remain separate backlog work.

### E3.3 Mission Creation
- Mission created only after accepted feasibility/confirmation
- external order reference preserved
- Mission lifecycle initialized

### E3.4 Delivery Request Query
- read request status with customer-scoped access
- external reference and creation timestamp
- Sprint 008 implements status lookup for received requests only
- Sprint 009 implements customer-scoped request history with keyset pagination and summary-only fields

### E3.5 Mission Query
- status
- promise
- assignment
- timeline
- customer-scoped access

## 6. Epic E4 — Shipment & Capability Model

### E4.1 Shipment Facts
- Sprint 037 adds optional package count, cubic-metre volume, and catalog temperature class to the existing shipment request facts, preserving legacy idempotency hashes when omitted.
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
- Sprint 038 adds an administrator-authored MODE_CAPABILITY policy definition containing unique, canonical mode identifiers. Initial identifiers are WALK, BIKE, and CAR; adding VAN, PICKUP, TRUCK, or another valid identifier needs no Mission-core change. Draft validation does not activate modes or imply network availability.
- Sprint 039 extends HARD policy preview to evaluate a candidate `mode` fact against effective MODE_CAPABILITY policies, fail closed on missing/invalid inputs, and exclude unsupported candidates.
- Sprint 040 adds an administrator preview that builds candidate mode identifiers from effective MODE_CAPABILITY policies and returns the modes that pass every applicable HARD policy.
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
- Sprint 041 defines a provider-neutral in-process contract for geocode, reverse geocode, route estimates, and route matrices with WGS84 coordinate and provider-response validation; no provider or network integration is selected.
- provider abstraction
- geocode
- reverse geocode
- route
- ETA
- route matrix

### E5.2 Location Validation
- Sprint 042 adds a reusable resolver: valid supplied coordinate pairs are authoritative; when coordinates are absent, address text is resolved through the provider-neutral Maps Gateway; a geocoding no-match remains unresolved. This capability is not yet wired into delivery intake.
- Coordinate validation
- Address fallback geocoding
- Zone resolution is deferred until E6.1 defines the zone registry and geometry.

### E5.3 Courier Location Ingestion
- Sprint 043 adds a provider-neutral GPS update contract, validation, a per-courier latest-position projection primitive, and freshness evaluation with a caller-supplied age limit; no threshold is invented.
- No durable storage or GPS endpoint is added yet. Authenticated ingestion and mission-aware tracking depend on Courier identity/auth (E9), Mission/Route lifecycle, and an approved tracking/retention policy.

## 8. Epic E6 — Zone & Capacity

### E6.1 Zone Model
- Sprint 044 defines the geometry-neutral City → Operating Zone → Micro-zone model and validates required parents, parent-kind relationships, and unique zone IDs.
- Geometry references, status labels, and capacity-policy IDs remain opaque metadata; the model does not define geometry, lifecycle transitions, coverage, or capacity.
- Courier preference and approval records are in E6.2.
- Geographic boundary enforcement and zone operations remain future work.

### E6.2 Courier Zone Approval
- preferred zones
- approved zones
- optional primary zone
- hard operating boundaries
- Sprint 045 adds a structural model for preferred zones, Mono-approved zones, and an optional primary zone. It verifies references against a supplied valid registry and requires approvals to be within preferences.
- Approval is recorded as domain data only; geographic boundary enforcement, zone lifecycle, persistence, authorization, and operations endpoints remain future work.

### E6.3 Capacity
- registered
- online
- effective
- Sprint 046 adds a per-zone snapshot for registered, online, and effective counts. It validates the zone reference, observation timestamp, and non-negative values without defining a formula or ordering between the figures.

### E6.4 Hana MVP Capacity Rule
```
registered_capacity_target = active_hana_stores * 10
```
- Sprint 047 implements this formula as a pure rule for the shared Zone courier pool. It validates a non-negative active-store count and does not allocate the target to individual stores or Zones.

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
- Sprint 048 adds an isolated route-distance criterion: compare a supplied route distance in metres against a caller-supplied configured maximum. Missing route distance remains unresolved; this criterion does not produce a final feasibility outcome.
- Sprint 049 adds a fail-closed gate requiring explicit results for all eight E7.1 criteria before outcome evaluation. Missing or unresolved evidence blocks readiness; this does not produce an outcome or Promise.
- Sprint 050 adapts the existing policy-based mode preview into the E7.1 eligible-modes criterion; incomplete evidence remains unresolved and this does not select a mode or create an outcome.
- Sprint 051 adds a fail-closed eligible-courier evidence criterion; a definitive failure requires complete candidate coverage, and live courier discovery remains future work.
- Sprint 052 compares the existing Zone effective-count snapshot with an explicitly supplied required count; no threshold, capacity conversion, or freshness rule is invented.
- Sprint 053 adds complete per-Zone active-route inventory evidence. Empty complete inventories are valid; missing or unresolved route states remain unresolved.
- Sprint 054 adds a validated Route Insertion criterion evidence envelope. Only complete, explicit evidence can pass or fail; simulation mechanics and operational outcomes remain future work.
- Sprint 055 compares a caller-supplied estimated delivery time with an explicit SLA deadline; missing or incomplete evidence remains unresolved and no SLA threshold is invented.
- Sprint 062 adds a canonical SHA-256 integrity envelope for criterion results and blocks Promise persistence when the required result digests are absent or invalid. This detects post-envelope mutation; it does not authenticate evidence sources or prove their assertions.
- Sprint 063 adds source ID and opaque attestation fields to the integrity envelope and a customer-bound verifier gate before persistence. The default verifier is intentionally unconfigured and blocks commits. Trusted source registration and a source-specific cryptographic verification method remain undecided.

### E7.2 Results
- ACCEPTED
- ALTERNATIVE_PROMISE
- REJECTED
- Sprint 056 applies the approved order: accept only when the requested promise passes the complete E7.1 gate; otherwise evaluate an explicit alternative, and reject only after a complete search proves no feasible alternative. Missing or incomplete evidence remains unresolved.

### E7.3 Promise
- committed_delivery_at
- version
- safety buffer
- reason evidence
- Sprint 057 adds a validated Promise commit proposal contract with delivery time, positive version, accepted outcome, explicit safety-buffer value and policy version, decision reasons, and policy-version evidence. It is validation-only; persistence, atomic commit, version sequencing, and buffer calculation remain future work.
- Sprint 058 atomically stores a tenant-scoped Promise version and its `promise.committed` / `promise.revised` outbox event. It enforces version sequence and idempotent replay by decision ID; delivery feasibility and buffer calculation remain upstream responsibilities.
- Sprint 059 adds an application service that runs the E7.2 evaluator and only persists a matching ACCEPTED or ALTERNATIVE_PROMISE with the exact evaluated delivery time. REJECTED, UNRESOLVED, and mismatched proposals never reach persistence.
- Sprint 060 adds an authenticated, tenant-scoped read endpoint for the current committed Promise of a delivery request. It returns the current version and committed time, while internal safety-buffer and decision-reason evidence remain private.
- Sprint 061 adds a paginated, tenant-scoped Promise version history endpoint. It exposes version, committed time, outcome, and record timestamp without returning internal decision evidence.
- Sprint 063 wires a fail-closed evidence-source verification boundary into the Promise commit service; no production source is trusted yet.
- Sprint 073 exposes the Promise commit service through an authenticated, tenant-scoped write endpoint. The server derives the decision ID from `Idempotency-Key`, evaluates E7.2, verifies evidence sources, and persists only a matching resolved proposal; the existing fail-closed verifier remains unchanged. PostgreSQL-backed HTTP coverage checks replay, changed-input conflict, a single persisted version, and the safe response shape.
- Sprint 074 records the full canonical commit request fingerprint with the Promise version and checks the tenant-scoped decision mapping after evidence-integrity validation but before source verification. Exact retries remain available during verifier outages; changed inputs conflict and invalid/tampered evidence is still rejected.

### Acceptance
- Mono never commits a Mission before proving feasibility.

## 10. Epic E8 — Pricing

### E8.1 Pricing Engine
- Sprint 064 implements a pure Hana Phase-1 distance-band calculator with the approved fare values and 50/50 buyer/seller split. Its distance intervals are lower-inclusive; the final interval includes the 5 km maximum. Other pricing dimensions and contract-specific pricing remain future work.
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
- Sprint 065 adds a tenant-scoped immutable Quote contract with positive version, explicit issue/expiry timestamps, policy-version evidence tied to the Hana tariff, and required audit-evidence IDs. Expiry is evaluated at the supplied instant; no default TTL is invented.
- Sprint 066 persists canonical Quote snapshots in PostgreSQL with tenant/request scoping, immutable version history, consecutive version checks, and replay detection. Expiry remains caller-supplied.
- Sprint 067 adds tenant-scoped current Quote and paginated version-history read APIs using delivery-requests.read. Responses omit policy-version and audit-evidence identifiers; expiry is reported at read time without extending the Quote.
- Sprint 068 adds an integrity envelope binding Hana pricing distance, evidence ID, observation time, source ID, and opaque source attestation. Source authenticity remains unverified until Mono selects a provider and configures the fail-closed verifier.
- Sprint 069 adds a Quote issuance application service for the initial version: it validates explicit expiry, verifies tenant-bound distance evidence, computes fare from the approved tariff, and stores a safe evidence reference. No HTTP endpoint or retry/idempotency behavior is added yet.
- Sprint 070 exposes initial Quote issuance through an authenticated endpoint. It computes evidence digests and fares server-side and atomically persists the Quote with a tenant-scoped Idempotency-Key mapping. Trusted source verification remains fail-closed until configured.
- Sprint 071 adds PostgreSQL-backed HTTP integration coverage for authorization, successful issue, identical retry, changed-input conflict, single-version persistence, and the public response boundary.
- Sprint 072 resolves a matching tenant-scoped Idempotency-Key before checking Quote expiry or re-verifying its source, so an exact committed retry remains replayable during verifier outages. Evidence integrity is still checked first; misses continue through the verified atomic issue path.
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
- internal managed API-key provisioning for existing active customers
- Sprint 022 adds administrator-triggered immediate key revocation with an immutable operations audit trail.
- Sprint 023 adds a secret-free, administrator-only read view of customer API key metadata.
- Sprint 025 adds an administrator-only, customer/key-scoped view of immutable API key revocation audit history.

### E16.2 Endpoints
- `GET /v1/branches` (`branches.read`)
- `POST /v1/delivery-requests` (`delivery-requests.write`, request intake only)
- `GET /v1/delivery-requests/{id}` (`delivery-requests.read`, customer-scoped request summary)
- `GET /v1/delivery-requests/{id}/promise` (`delivery-requests.read`, customer-scoped current committed Promise)
- `GET /v1/delivery-requests/{id}/promise/versions` (`delivery-requests.read`, paginated customer-scoped Promise history)
- Sprint 073 adds `POST /v1/delivery-requests/{id}/promise` (`delivery-requests.write`, evaluator-backed, source-verified and idempotent Promise commit)
- Sprint 074 makes Promise retries request-bound and replayable before source-verifier calls, while preserving evidence-integrity checks.
- Sprint 070 adds `POST /v1/delivery-requests/{id}/quote` (`delivery-requests.write`, tenant-scoped, idempotent first Quote issuance)
- `GET /v1/delivery-requests` (`delivery-requests.read`, paginated history)
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

## 25. Epic E23 — Merchant Mobile App

Merchant Mobile is a distinct operational surface for business users and must not be implemented as a responsive copy of Merchant Dashboard.

### P0 capabilities
- authentication / tenant bootstrap
- branch-aware Home
- active deliveries
- Delivery Detail
- committed promise vs current ETA
- Create Delivery
- feasibility
- quote + confirm
- notifications
- exception context
- cancellation where allowed
- contextual support
- empty / loading / error / no-feasibility / success states
- mobile observability and security baseline

### P1 capabilities
- finance summary
- profile
- notification preferences
- security / sessions refinements

### Web-first administration
- organization members / access
- integrations / webhooks
- visual identity / co-branding
- advanced workspace configuration
- deep billing / reconciliation
- bulk administration

Detailed refined backlog:
- `docs/23-merchant-mobile-backlog-v1.md`

### Acceptance
- Mobile and Web use the same Mono Core rules.
- Mobile never invents separate Mission, Promise, cancellation or authorization logic.
- Notification opens exact operational context when available.
- Branch context is explicit before operational actions.
- Committed Promise and current ETA are visually and semantically distinct.

## 26. Deferred Epics

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

## 27. Backlog Rule

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
