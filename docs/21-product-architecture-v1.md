# Mono Product Architecture v1

## Product Surfaces

Mono MVP has four customer/operator-facing product surfaces:

1. **B2B API**
   - for Hana and other integrated customers
   - delivery request, promise, tracking, cancellation, evidence, webhooks

2. **Merchant Dashboard**
   - for businesses without their own app
   - manual delivery creation, quotes, tracking, history, branches, billing

3. **Courier App**
   - for Mono couriers
   - availability, offers, pickup, route, delivery, wallet

4. **Operations Console**
   - for Mono operations
   - mission control, routes, couriers, zones, SLA, exceptions, re-dispatch

All surfaces use the same shared Mono Core.

## Product Rule

```
Channel != Business Logic
```

API, Dashboard and internal applications must not implement separate logistics rules.

## Information Architecture

### Operations Console
- Overview
- Missions
- Routes
- Couriers
- Vehicles
- Zones & Capacity
- SLA / Exceptions
- Customers
- Policies / Configurations
- Finance
- Integrations
- Audit

### Courier App
- Home / Availability
- Mission Offers
- Active Route
- Pickup
- Delivery
- Exceptions
- Earnings
- Wallet / Withdrawals
- Profile / Documents

### Merchant Dashboard
- Overview
- New Delivery
- Active Deliveries
- Tracking
- Delivery History
- Branches
- Pricing / Plan
- Billing
- Users
- Integrations (where enabled)

## MVP Priority

1. Operations Console
2. Courier App
3. Hana API integration
4. Merchant Dashboard

The API is built in parallel with the operational products; Hana does not require a Mono customer-facing UI.

## Product Design Principle

Product design must expose operational truth clearly:

- current Mission state
- committed promise
- current ETA
- assignment
- route
- SLA risk
- exception
- financial status
- action history

The UI must never hide whether data is estimated, committed, delayed, or manually overridden.
