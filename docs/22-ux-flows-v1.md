# Mono UX Flows v1

## 1. Hana API Flow

```
Hana Order Confirmed
-> Create Delivery Request
-> Mono evaluates feasibility
-> Quote + Promise
-> Hana confirms / auto-confirms
-> Mission created
-> Courier assigned
-> Pickup
-> STORE_TO_COURIER handoff
-> In transit
-> Delivered
-> Webhook updates to Hana
```

## 2. Merchant Manual Delivery Flow

```
Merchant Login
-> New Delivery
-> Select Branch / Pickup
-> Enter Recipient + Destination
-> Enter Shipment
-> Review Fare + Promise
-> Confirm
-> Mission created
-> Track
-> Delivered / Failed
-> History / Billing
```

## 3. Courier Core Flow

```
Login
-> Go Online
-> Receive Offer
-> Accept
-> Navigate to Pickup
-> Arrived Pickup
-> Handoff
-> Active Route
-> Arrived Dropoff
-> Confirm Recipient
-> Delivered
-> Earnings Credited
```

## 4. Courier Batch Flow

```
Active Route
-> Mono proposes additional Mission
-> Route is re-simulated
-> Promise constraints pass
-> Courier receives/accepts updated route
-> Additional pickup/dropoff stops
-> All Missions delivered within promise
```

## 5. Operations Mission Control Flow

```
Operations Dashboard
-> SLA Risk / Exception detected
-> Open Mission
-> Inspect timeline + route + courier + policy decision
-> Choose allowed action
   -> re-dispatch
   -> contact courier
   -> mark operational exception
   -> controlled override
-> Audit action
-> Monitor result
```

## 6. Cancellation Flow

```
Customer cancellation request
-> Mono checks policy + handoff state
-> Before handoff: cancel Mission
-> At/after handoff: reject cancellation
-> Customer system uses incident flow if needed
```

## 7. Store Delay Flow

```
Courier Arrived Pickup
-> 3 minute dwell timer
-> Order not handed over
-> STORE_PREPARATION_DELAY
-> Mission / route re-evaluated
-> SLA risk if applicable
-> Operations visibility
```

## 8. Failed Delivery Flow

```
Arrived Dropoff
-> Recipient cannot be confirmed / unavailable / address issue
-> Delivery exception
-> Apply customer policy
-> retry / wait / return / manual review
-> webhook customer
```

## 9. Courier Withdrawal Flow

```
Earnings credited
-> Available Balance
-> Request Withdrawal
-> Check daily limit
-> Validate destination
-> Payout
-> Reconcile
```

## 10. UX Principle

Every critical flow must show:

- current state
- next expected action
- promise/SLA context
- reason when blocked
- customer/courier impact
- audit history for operational actions

## 11. Incoming Call / Customer Context Flow

```
Inbound Call
-> Telephony / VoIP sends caller ID
-> Mono resolves caller
-> Operations Console automatically opens caller context
-> Show active order(s) + Mission context
-> Operator reviews live delivery state
-> Open Mission Detail / incident / allowed action
```

Recognized caller:
- no manual search required before context appears
- show caller identity, customer order ID, Mission, ETA / Promise, SLA, courier, route, origin, destination and latest event
- if more than one active order exists, show all active matches and emphasize the most relevant one

Unknown caller:
- automatically open an unmatched-caller state
- preserve incoming phone number
- provide immediate fallback search by order ID, Mission ID, name or phone

UX rule:
**Incoming call -> context first, search second.**

## 12. Merchant Mobile — Daily Operations Flow

```
Open App
-> Home
-> See active deliveries + risk / exception count
-> Open delivery needing attention
-> Review promise + current ETA + latest event
-> Take allowed action / contact support
-> Return to Home
```

Home must prioritize operational attention over analytics.

## 13. Merchant Mobile — Quick Create Delivery Flow

```
Home / Deliveries
-> Create Delivery
-> Select Pickup Branch
-> Enter Recipient
-> Enter Destination
-> Enter Shipment Details
-> Set Ready Time if applicable
-> Mono evaluates feasibility
-> Show Fare + Delivery Promise
-> Confirm
-> Mission Created
-> Delivery Detail
```

UX rules:
- preserve entered data if feasibility fails
- explain the blocking reason
- never present an estimated time as a committed promise
- confirmation must clearly show pickup, destination, fare and promise

## 14. Merchant Mobile — Active Delivery Tracking Flow

```
Home / Deliveries / Notification
-> Delivery Detail
-> Current State
-> Committed Promise
-> Current ETA
-> Latest Event
-> Courier / Route Context where customer-visible
-> Timeline
-> Allowed Actions
```

The default detail view must answer:
- where is the delivery now?
- what was promised?
- is it on track?
- what happens next?
- does the merchant need to act?

## 15. Merchant Mobile — Exception Flow

```
Delay / Exception Detected
-> Push / In-app Notification
-> Tap Notification
-> Exact Delivery Exception Context
-> Show impact on Promise
-> Show reason if customer-visible
-> Show current Mono action / next step
-> Merchant allowed action if any
-> Resolution
-> Timeline updated
```

UX rule:
**Notification -> exact context -> action.**
Never land the merchant on a generic notification inbox when an actionable delivery context exists.

## 16. Merchant Mobile — Cancellation Flow

```
Delivery Detail
-> Cancel Delivery
-> Mono checks cancellation policy + handoff state
-> If allowed: show consequence / fee if any
-> Confirm Cancellation
-> Mission Cancelled
-> Timeline + financial state updated
```

If cancellation is not allowed:
- explain why
- preserve the delivery context
- offer support / incident path where relevant

## 17. Merchant Mobile — Branch Switching Flow

```
Home
-> Current Branch / Workspace
-> Open Switcher
-> Select Authorized Branch
-> Home refreshes for selected branch
-> New Delivery defaults to selected branch
```

The active branch must remain visible enough to prevent accidental creation from the wrong origin.

## 18. Merchant Mobile — Notifications Flow

```
Operational Event
-> Push / In-app Notification
-> Notification category + delivery identity
-> Tap
-> Open exact delivery / billing / workspace context
```

Priority:
1. exception / SLA risk
2. cancellation / failed delivery
3. assignment / pickup / delivered
4. finance / operational notice
5. general information

## 19. Merchant Mobile — Finance Summary Flow

```
More / Home Summary
-> Finance
-> Current billing summary
-> Recent charges / deliveries
-> Outstanding amount / status where applicable
-> Open detailed billing on web when deep reconciliation is required
```

Mobile is for operational financial visibility, not full accounting administration.

## 20. Merchant Mobile — Support Flow

```
Delivery Detail / More
-> Support
-> Delivery context automatically attached when entered from a delivery
-> Choose issue type
-> Start support request / contact channel
-> Track status
```

The merchant should not have to re-enter identifiers already known by the app.

## 21. Merchant Mobile Navigation Rule

Primary navigation should optimize frequency, not mirror the web sidebar.

Recommended primary navigation:

- Home
- Deliveries
- Create
- Notifications
- More

Deep administration remains web-first.

## 22. Merchant Mobile UX Principles

- Persian true RTL
- Vazirmatn
- one-handed mobile interaction
- no desktop table patterns squeezed onto mobile
- action-first home
- promise and ETA always distinguishable
- operational risk visually prioritized without excessive alarm color
- notifications deep-link to exact context
- forms preserve progress
- branch/workspace context remains explicit
- destructive actions require confirmation
- every blocked action explains why
- data and actions use the same Mono Core rules as web and API
