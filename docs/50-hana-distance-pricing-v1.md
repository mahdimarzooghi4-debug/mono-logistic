# Hana Distance Pricing v1

## Approved tariff

The calculator uses the Phase-1 Hana distance tariff, with distance supplied in metres and fare represented as integer toman:

| Distance interval | Fare |
|---|---:|
| `0 <= d < 1,000 m` | 20,000 toman |
| `1,000 <= d < 2,000 m` | 27,500 toman |
| `2,000 <= d < 3,000 m` | 35,000 toman |
| `3,000 <= d < 4,000 m` | 42,500 toman |
| `4,000 <= d <= 5,000 m` | 50,000 toman |

The intervals make exact band boundaries deterministic: for example, exactly 1,000 metres uses the 1–2 km fare. Distances below zero or above 5,000 metres are rejected; the calculator does not infer a fare outside Hana's approved range.

## Result

`HanaDistancePricingV1.Calculate` returns the policy version, assessed distance, band start, total fare, buyer share, and seller share. Buyer and seller each pay 50%. The current tariff values divide evenly, so no rounding rule is needed.

## Scope limits

The fare is independent of Walk, Bike, or Car as specified in the Hana Phase-1 pricing decision. This calculator does not calculate quote expiry, courier compensation, Mono commission, VAT, route batching, or settlement. It does not apply weight, zone, flat-fee, contract, or minimum/maximum modifiers beyond enforcing the approved 5 km and 50,000-toman tariff cap. Those capabilities need separate product and technical decisions.
