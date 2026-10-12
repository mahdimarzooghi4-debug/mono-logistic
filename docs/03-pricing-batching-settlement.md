# Pricing, Batching & Settlement — Phase 1

## 1. Hana Delivery Fare

The delivery fare is based on distance and is independent of whether Mono executes the Mission by Walk, Bike, or Car.

Initial MVP tariff:

| Distance | Delivery Fare |
|---|---:|
| 0–1 km | 20,000 toman |
| 1–2 km | 27,500 toman |
| 2–3 km | 35,000 toman |
| 3–4 km | 42,500 toman |
| 4–5 km | 50,000 toman |

Maximum fare in Hana phase 1:

> **50,000 toman**

The delivery fee is split equally between Hana's buyer and seller:

```
Buyer share = 50%
Seller share = 50%
```

## 2. Money Flow

```
Buyer + Seller
     -> Hana
     -> Mono
     -> Courier
```

The courier is financially settled by Mono, not directly by Hana.

## 3. Mono Commission

Mono commission:

```
10% of Mission fare
```

VAT:

```
10% of Mono commission
```

Therefore total deduction from the courier-side Mission amount is effectively:

```
11% of Mission fare
```

Courier net earning:

```
Courier Net = Mission Fare × 89%
```

Examples:

| Fare | Mono Commission | VAT | Courier Net |
|---:|---:|---:|---:|
| 20,000 | 2,000 | 200 | 17,800 |
| 30,000 | 3,000 | 300 | 26,700 |
| 40,000 | 4,000 | 400 | 35,600 |
| 50,000 | 5,000 | 500 | 44,500 |

## 4. Batching

The economic strength of Mono comes from route-level batching.

A courier may carry up to:

> **5 Missions per Route**

only if all delivery promises remain feasible.

Before adding a Mission to an existing Route, Mono must re-simulate the entire Route.

Batch approval requires:

- courier load capacity remains valid,
- approved operating zone remains valid,
- pickup timing remains valid,
- pickup dwell rule remains feasible,
- no existing Mission misses its committed delivery time,
- the new Mission can also be delivered within its committed time.

## 5. Micro-batching

Mono should prioritize batches where:

- pickups are the same store or nearby,
- dropoffs are in the same street, block, building cluster, or micro-zone,
- detour is low,
- total route completion time remains efficient.

This is especially important for Walk couriers.

Example:

Five small grocery orders in the same micro-zone can produce several times the income of a single-order trip while still remaining cheap for each Hana order.

## 6. Courier Balance & Withdrawal

After a Mission is completed:

```
Net earning -> Courier Available Balance
```

Funds do not need to be sent immediately to the bank account.

The courier may retain earnings in Mono and request withdrawal on demand.

Rule:

> **Maximum 3 withdrawals per day**

Suggested wallet fields:

- available_balance
- pending_balance
- withdrawals_today
- remaining_withdrawals_today
- transaction_history
