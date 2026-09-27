# Hana × Mono — Phase 1 Operating Model

## 1. Scope

- Service: Urban grocery delivery
- Maximum pickup-to-dropoff distance: **5 km**
- Customer: Hana
- Goods: primarily supermarket orders carried in bags
- Execution modes:
  - Walk
  - Bike
  - Car

## 2. Mission Trigger

Mono should enter the process **before the order is physically ready**.

Target flow:

```
Order confirmed in Hana
  -> Mission created in Mono
  -> Ready Time received / estimated
  -> Dispatch planning
  -> Courier moves toward store
  -> Order becomes ready
  -> Courier arrives near Ready Time
  -> Pickup
  -> Delivery
```

The goal is synchronization:

```
Courier Arrival ~= Order Ready Time
```

## 3. Pickup Hard SLA

From courier arrival at the store until pickup completion:

> **Pickup Dwell Time <= 3 minutes**

This is a hard operating rule.

If the courier arrives and the order is not handed over within 3 minutes, Mono records a store-side preparation delay.

## 4. Bag-based Shipment Model

Hana orders are modeled as bag-based grocery shipments.

Primary shipment fields:

- bag_count
- estimated_total_weight
- load_class
- contains_liquid
- contains_fragile
- keep_upright
- later: temperature_sensitive

Initial load classes:

- **S**: one light bag
- **M**: two to three bags or medium load
- **L**: more than three bags or heavy/bulky load

## 5. Mode Selection

Mode is determined by Mono, not Hana.

Indicative rules:

### Walk
- short distance
- small/light shipment
- best for dense micro-zones

### Bike
- default mode for many urban orders
- suitable through the 0–5 km range when load permits

### Car
- heavy/bulky orders
- large bag count
- special handling or cases where Walk/Bike are not suitable

Mode selection must consider:

```
Distance
+ Bag Count
+ Weight
+ Load Class
+ Content Risk
+ SLA
+ Current Capacity
```

## 6. Feasibility Engine

Before accepting an order Mono checks:

1. order validity,
2. distance <= 5 km,
3. eligible modes,
4. eligible couriers,
5. courier approved zone,
6. courier trust eligibility,
7. courier capacity,
8. current route,
9. possible batching,
10. route simulation,
11. delivery promise feasibility.

Output:

- Accepted
- Alternative Promise
- Rejected

Example response contract:

```
accepted
committed_delivery_at
delivery_fee
mission_id
```

Mono must not commit a delivery time before route feasibility is proven.

## 7. Mission Lifecycle

Normal states:

```
RECEIVED
-> VALIDATING
-> PLANNING
-> PROMISED
-> SEARCHING_COURIER
-> COURIER_ASSIGNED
-> EN_ROUTE_TO_PICKUP
-> ARRIVED_PICKUP
-> PICKED_UP
-> IN_TRANSIT
-> ARRIVED_DROPOFF
-> DELIVERED
-> COMPLETED
-> SETTLEMENT_PENDING
-> SETTLED
```

Exception states may include:

- WAITING_FOR_ORDER
- REDISPATCHING
- SLA_AT_RISK
- DELIVERY_FAILED
- RETURNING
- INCIDENT
- CANCELLED

## 8. Mission vs Route

A Mission is one Hana order.

A Route is a courier execution plan and may contain multiple Missions.

```
Order -> Mission -> Route -> Courier -> Delivery -> Settlement
```

A Route may contain up to **5 Missions**, provided every Mission remains inside its committed delivery promise.
