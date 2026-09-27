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
