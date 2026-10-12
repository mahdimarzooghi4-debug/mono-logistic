# Courier, Trust, Zones & Capacity — Phase 1

## 1. Courier Types

Phase 1 supports:

- Walk
- Bike
- Car

Mono does not require its own fleet. Couriers participate in Mono's operating network.

## 2. Courier Onboarding

Base onboarding data:

- full name
- mobile number
- national identity
- profile photo
- banking / settlement information
- preferred operating zones
- courier mode
- acceptance of Mono contracts and rules

Bike/Car may require vehicle and driving-related documents.

A courier must be approved by Mono before receiving Missions.

Lifecycle:

```
REGISTERED
-> IDENTITY_VERIFIED
-> DOCUMENTS_SUBMITTED
-> TRUST_REQUIREMENTS_VERIFIED
-> MONO_APPROVED
-> ACTIVE
```

## 3. Trust & Security

Mono may require:

- identity verification,
- verified banking information,
- contract acceptance,
- certificate of no criminal record,
- or an approved electronic promissory note / guarantee.

Possible trust levels:

### Level 1
- identity
- bank information
- contract
- Mono approval

### Level 2
- Level 1
- approved background check or electronic guarantee

### Level 3
- strong performance history
- valid guarantee
- no serious incidents

Risk eligibility is a hard assignment constraint.

A courier who does not satisfy the required trust level must not receive that Mission even if they are geographically the best candidate.

## 4. Performance Profile

Mono should build a courier operational profile from:

- completed Missions
- on-time rate
- acceptance rate
- cancellation rate
- completion rate
- pickup dwell behavior
- incident history

## 5. Operating Zones

A Zone is a **hard operating boundary**, not just an analytics concept.

Flow:

```
Courier selects preferred zones
-> Mono evaluates
-> Mono approves zones
-> Courier may operate only inside approved zones
```

Suggested fields:

- preferred_zones
- approved_zones
- primary_zone
- mode
- zone_status
- available_in_zone

A courier must not receive or execute a Mission outside approved operating zones.

## 6. Micro-zone Architecture

The city should be divided into fixed micro-zones.

Courier operating areas are constructed from those micro-zones.

This supports:

- Walk micro-delivery,
- supply-demand measurement,
- route batching,
- geographic analytics,
- future capacity optimization.

## 7. Base Zone Capacity Rule

MVP capacity is driven by Hana's active stores.

> **Base Courier Capacity = Active Stores × 10**

Example:

```
8 active stores -> target registered capacity = 80 couriers
```

The 10 couriers are not reserved to an individual store. They belong to a shared Zone courier pool.

Track three separate capacity figures:

### Registered Capacity
Approved couriers allowed to operate in the Zone.

### Online Capacity
Couriers currently online.

### Effective Capacity
Couriers currently able to accept a new Mission, including couriers whose existing Route is about to complete.

The factor of 10 is an MVP planning rule and can later be recalibrated using real operating data.

## 8. Zone Health

Suggested states:

- HEALTHY
- WARNING
- CRITICAL
- OVER_CAPACITY

Zone health influences the Promise Engine.

Mono should not promise the same delivery time when effective capacity has materially deteriorated.
