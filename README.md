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
- [Unified B2B Policy & Capability Model](docs/06-b2b-policy-capability-model.md)

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

- [Mono Policy Framework v1](docs/07-policy-framework.md)
- [Mono Policy Catalog v1](docs/08-policy-catalog-v1.md)
- [Mono Domain Model v1](docs/09-domain-model-v1.md)
- [Mono Service Boundaries v1](docs/10-service-boundaries-v1.md)
- [Mono Event Model v1](docs/11-event-model-v1.md)
- [Mono B2B API Contract v1](docs/12-api-contract-v1.md)
- [Mono Data Architecture v1](docs/13-data-architecture-v1.md)
- [Mono Runtime Decision Architecture v1](docs/14-runtime-decision-architecture-v1.md)