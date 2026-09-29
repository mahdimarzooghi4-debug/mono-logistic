# Mono Product Architecture v1

## Product Surfaces

Mono has five customer/operator-facing product surfaces:

1. **B2B API**
   - for Hana and other integrated customers
   - delivery request, promise, tracking, cancellation, evidence, webhooks

2. **Merchant Dashboard**
   - web workspace for business administrators and operational teams
   - manual delivery creation, active deliveries, tracking, history, branches, billing, members, integrations, configuration and co-branding

3. **Merchant Mobile App**
   - mobile-first operational companion for business owners, branch managers and day-to-day operators
   - optimized for fast delivery creation, live status, tracking, notifications, exceptions and lightweight branch/finance visibility
   - not a responsive copy of the Merchant Dashboard

4. **Courier App**
   - for Mono couriers
   - availability, offers, pickup, route, delivery, wallet

5. **Operations Console**
   - for Mono operations
   - mission control, routes, couriers, zones, SLA, exceptions, re-dispatch

All surfaces use the same shared Mono Core.

## Product Rule

```
Channel != Business Logic
```

API, Dashboard, mobile applications and internal applications must not implement separate logistics rules.

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
- Users & Access
- Notifications
- Visual Identity / Co-branding
- Integrations
- Workspace Settings

### Merchant Mobile App
Primary navigation:
- Home
- Deliveries
- Create Delivery
- Notifications
- More

Home:
- branch / workspace switcher where authorized
- active deliveries summary
- deliveries at risk / exceptions
- recent delivery activity
- quick create delivery action
- key financial summary

Deliveries:
- Active
- Scheduled / Pending where enabled
- Completed
- Failed / Cancelled
- Search and practical filters

Delivery Detail:
- Mission / delivery status
- recipient and destination
- pickup branch
- committed promise
- current ETA
- courier / route context where customer-visible
- live timeline
- cancellation eligibility
- exception / incident state
- allowed merchant actions
- contact / support entry point

Create Delivery:
- select pickup branch
- recipient
- destination
- shipment details
- readiness time where applicable
- quote + delivery promise
- final confirmation
- creation result

Notifications:
- assignment / pickup / delivery events
- delay and SLA-risk alerts
- delivery exceptions
- cancellation / resolution updates
- billing / operational notices where appropriate

More:
- Branches
- Finance summary
- Team / profile
- Support
- Settings

## Merchant Web vs Mobile Responsibility

Merchant Mobile is not a smaller Merchant Dashboard.

### Mobile-first
- create a delivery quickly
- monitor active deliveries
- open a delivery from a notification
- understand promise / ETA / risk immediately
- respond to an exception
- cancel when policy allows
- switch branch / workspace where authorized
- view lightweight finance and operational summaries
- contact support

### Web-first
- full organization configuration
- detailed members and access management
- visual identity / co-branding configuration
- integration and webhook configuration
- deep billing / invoices / reconciliation
- advanced reporting
- bulk workflows
- detailed workspace settings

Any feature available on both surfaces must read/write the same underlying domain state and policy decisions.

## Merchant Mobile Product Principles

1. **Action-first**
   - the primary screen must answer: what needs attention now?

2. **Promise-first**
   - committed promise and current ETA must never be visually ambiguous.

3. **Notification-to-context**
   - tapping a notification opens the exact delivery / exception context, not a generic list.

4. **Branch-aware**
   - users with multiple branches must always know which branch/workspace they are acting in.

5. **One-handed operational use**
   - critical actions must be reachable without dense desktop-style navigation.

6. **Progressive disclosure**
   - show essential operational state first; deeper detail is available on demand.

7. **Shared truth**
   - mobile never creates a separate workflow or logistics rule from web/API/Core.

## MVP Priority

1. Operations Console
2. Courier App
3. Hana API integration
4. Merchant Dashboard
5. Merchant Mobile App

The API is built in parallel with the operational products; Hana does not require a Mono customer-facing UI.

Merchant Mobile can follow the Merchant Dashboard foundation but should reuse domain contracts, not desktop layouts.

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

## Automatic Caller Context

The Operations Console must support an automatic incoming-call context state.

When the telephony / VoIP / call-center integration receives an inbound caller ID, Mono resolves that number against known customer, recipient, order and Mission data and automatically opens the relevant context for the operator.

The operator must not need to search manually before seeing the caller's active logistics context.

For a recognized caller, the automatic context shows:

- caller / recipient name
- phone number
- customer account
- customer order ID
- active Mission ID
- current Mission state
- committed promise and current ETA
- SLA health
- assigned courier and active Route
- pickup branch / origin
- destination
- latest operational event
- cancellation eligibility
- open incident / exception if any

If multiple active orders match the caller, Mono shows the active-order list and highlights the most recent / most operationally relevant order.

If no match is found, the call state still opens automatically with the incoming phone number and a fast fallback search.

The automatic caller context is an Operations Console state, not a separate logistics core. Telephony is an integration channel; Mission and Order truth remain in the shared Mono Core.
