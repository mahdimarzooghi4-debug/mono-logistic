# Mono Logistics

Mono is a logistics technology and operations platform.

## Vision

Mono aims to build a unified logistics orchestration layer for businesses across urban, intercity, ground, air, and sea logistics.

Mono is **not defined as a courier company or carrier**. Its core value is to receive a logistics request, determine the best fulfillment plan, orchestrate execution, control SLA, track delivery, and settle with couriers/providers.

## Phase 1

Phase 1 focuses exclusively on **urban logistics**.

The first customer is **Hana**, whose urban logistics operations are outsourced to Mono.

Core flow:

```
Hana
  -> Mono Mission
  -> Feasibility & Promise
  -> Mode / Courier / Route Decision
  -> Pickup
  -> Delivery
  -> Settlement
```

## Business Foundation

- [Business Architecture](docs/01-business-architecture.md)
- [Hana Phase 1 Operating Model](docs/02-hana-phase-1-operating-model.md)
- [Pricing, Batching & Settlement](docs/03-pricing-batching-settlement.md)
- [Courier, Trust, Zones & Capacity](docs/04-courier-zones-trust-capacity.md)
- [Maps, GPS, Location & Routing](docs/05-maps-location-routing.md)
- [Hana Urban Delivery Configuration](docs/customers/hana/urban-delivery-configuration.md)

## Current Phase-1 Decisions

- Urban service radius: maximum 5 km
- Modes: Walk, Bike, Car
- Maximum 5 Missions per Route when all committed delivery times remain feasible
- Pickup dwell hard SLA: maximum 3 minutes
- Pricing by distance only, capped at 50,000 toman
- Buyer/seller delivery-cost split: 50/50
- Mono commission: 10% of Mission fare + VAT on the commission
- Courier net earnings credited to Mono balance after Mission completion
- Maximum 3 withdrawals per courier per day
- Couriers operate only inside Mono-approved zones
- Base Zone capacity: 10 approved couriers per active Hana store
- Mono owns the delivery promise and commits only after feasibility / route simulation

## Repository Role

This repository is the source of truth for Mono's:

```
Business
-> Technical
-> Scrum / Product Backlog
-> Sprint
-> Code
-> Code Review
-> Stage
-> QA / Testing
-> Release Approval
-> Production
-> Monitoring
-> Improvement
```

Business decisions should be documented here before they become product or implementation decisions.
