# Mono Observability Architecture v1

## 1. Purpose

Mono operates real-time logistics. Observability must explain:

- what happened,
- where it happened,
- why a decision happened,
- whether customer promises are at risk,
- whether infrastructure is healthy,
- whether providers/maps/webhooks are degraded.

## 2. Observability Pillars

Mono should capture:

- Logs
- Metrics
- Traces
- Domain Events
- Audit Records
- Decision Records

These are related but not interchangeable.

## 3. Correlation

Every inbound request should receive:

```
request_id
correlation_id
```

Correlation should flow through:

```
API
-> Feasibility
-> Pricing
-> Promise
-> Mission
-> Assignment
-> Route
-> Delivery
-> Settlement
```

## 4. Structured Logging

Logs should be structured.

Suggested fields:

```
timestamp
level
service
environment
request_id
correlation_id
customer_id
mission_id
route_id
courier_id
event_type
message
error_code
duration_ms
```

Avoid logging secrets or unnecessary personal data.

## 5. Metrics Categories

### API
- request rate
- latency
- error rate
- rate-limit rate

### Mission
- created
- accepted
- rejected
- completed
- failed
- cancelled

### Promise
- on-time delivery
- SLA-at-risk count
- breached promise count

### Pickup
- arrival-to-handoff dwell
- store preparation delay
- pickup failure

### Assignment
- time to assign
- offers per Mission
- rejection rate
- timeout rate
- re-dispatch rate

### Routing
- route simulation latency
- route changes
- batch size
- average detour
- route density

### Location
- GPS freshness
- stale courier positions
- location ingestion lag

### Finance
- earning posting success
- settlement success/failure
- withdrawal success/failure
- reconciliation mismatch

### Integration
- webhook success rate
- retry rate
- dead-letter count
- customer API error rate

## 6. Core Operational KPIs

Initial KPIs:

- On-Time Delivery Rate
- Pickup Dwell P50/P95
- Courier Assignment Time
- Acceptance Rate
- Mission Completion Rate
- Batch Size
- Missions per Route
- Courier Earnings per Hour
- Zone Effective Capacity
- SLA-at-Risk Rate
- Delivery Failure Rate
- Cancellation Rate
- Incident Rate
- Mission Gross Margin

## 7. Tracing

Distributed tracing should capture critical paths.

Example:

```
POST /delivery-requests
  -> configuration lookup
  -> policy evaluation
  -> maps route matrix
  -> feasibility
  -> pricing
  -> promise
  -> mission commit
```

Trace spans should include decision IDs and external dependency latency.

## 8. Decision Observability

For runtime decisions, operations must be able to inspect:

- normalized inputs
- applied policies
- rejected candidates
- selected candidate
- route simulation result
- promise result
- reason codes
- decision latency

This is stronger than ordinary application logging.

## 9. External Dependency Monitoring

Monitor:

- map provider
- routing provider
- SMS provider
- payment/payout provider
- external logistics provider
- webhook destinations

Track:

- uptime
- latency
- timeout
- error rate
- quota exhaustion
- fallback usage

## 10. Zone Health

Zone monitoring should include:

- registered couriers
- online couriers
- effective capacity
- active Missions
- unassigned Missions
- assignment latency
- SLA risk
- capacity trend

## 11. Courier Network Health

Metrics:

- online couriers
- active couriers
- idle couriers
- acceptance rate
- cancellation rate
- completion rate
- on-time rate
- stale GPS rate

## 12. Event Pipeline Health

Track:

- outbox backlog
- event publish lag
- consumer lag
- duplicate event rate
- dead-letter count
- failed consumer count

## 13. Database Health

Track:

- connection pool usage
- query latency
- slow queries
- lock contention
- replication lag
- disk usage
- transaction failure rate

## 14. Cache Health

Track:

- hit rate
- eviction rate
- memory pressure
- stale data incidents
- unavailable cache fallback

## 15. Alerting

Alerts should be actionable.

Examples:

### Critical
- tenant isolation/security breach indicator
- ledger posting failure burst
- database unavailable
- Mission state corruption
- major API outage

### High
- map provider outage
- assignment failure spike
- webhook outage for major customer
- SLA breach spike
- zone capacity collapse

### Warning
- rising latency
- queue backlog
- stale GPS
- declining acceptance rate

## 16. SLO Framework

Each major capability should define:

- availability SLO
- latency SLO
- correctness SLO where measurable

Initial examples:

- B2B API availability
- delivery request evaluation latency
- webhook delivery reliability
- GPS freshness
- ledger posting success
- Mission state consistency

Exact numeric targets should be set after load testing and MVP baseline.

## 17. Error Budgets

Once SLOs are defined, use error budgets to balance:

- feature velocity
- reliability work
- provider dependency risk

## 18. Dashboards

Recommended dashboards:

### Executive Operations
- Missions
- on-time rate
- SLA risk
- margin
- active zones

### Dispatch
- unassigned Missions
- courier supply
- route load
- pickup delays

### Integration
- API latency/errors
- webhook failures
- customer-specific issues

### Finance
- ledger posting
- settlement
- payout/withdrawal

### Infrastructure
- service health
- database
- queue
- cache
- external providers

## 19. Customer-specific Views

Large B2B customers may receive scoped operational dashboards.

They must only expose that customer's data.

## 20. Log Retention

Retention should differ by class:

- security logs
- audit logs
- application logs
- debug logs
- telemetry

Do not keep verbose debug logs indefinitely.

## 21. PII in Observability

Logs/metrics should avoid:

- full phone numbers
- national IDs
- bank details
- secrets
- unnecessary addresses

Use identifiers and masked values.

## 22. Incident Timeline

For serious incidents, operators should be able to reconstruct:

```
Request
-> Decision
-> Promise
-> Assignment
-> Pickup
-> Route Changes
-> Delivery
-> Finance
```

using correlation IDs and domain records.

## 23. Observability Ownership

Each service/module owns its own telemetry.

A shared observability platform aggregates and visualizes.

## 24. Core Principle

> **If Mono cannot explain a logistics decision or detect an SLA risk in time, the system is not operationally observable.**
