# Mono Logistics

Mono is an independent logistics technology and operations business. Hana is its first customer; Mono's ownership and long-term scope are independent of any one customer.

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

## Visual Identity

- [Mono brand system in Figma](https://www.figma.com/design/TsYT48l66ee34uvOoS9XI2/mono-lgestic?node-id=0-1)
- Figma includes English and Persian logo lockups, icon, usage rules, brand handoff assets, and typography.
- Primary cyan: `#00A8C6`; tagline gray: `#4A4F57`; reverse dark: `#090E1C`; white: `#FFFFFF`.
- Typography: Inter for English/Latin and Vazirmatn for Persian.

## Product UI Designs

The same Figma file also contains product screen designs:

- [Operations Console](https://www.figma.com/design/TsYT48l66ee34uvOoS9XI2/mono-lgestic?node-id=66-2)
- [Onboarding Console](https://www.figma.com/design/TsYT48l66ee34uvOoS9XI2/mono-lgestic?node-id=130-2)
- [Courier App](https://www.figma.com/design/TsYT48l66ee34uvOoS9XI2/mono-lgestic?node-id=169-2)
- [Merchant Dashboard](https://www.figma.com/design/TsYT48l66ee34uvOoS9XI2/mono-lgestic?node-id=207-2)
- [Merchant Mobile](https://www.figma.com/design/TsYT48l66ee34uvOoS9XI2/mono-lgestic?node-id=358-2), including critical/supporting flows, account and settings, system states, and a QA summary.

These are design artifacts; displayed sample values are not operational data. Product behavior and release scope remain governed by the approved product decisions and roadmap.

## Business Foundation

- [Business Architecture](docs/01-business-architecture.md)
- [Hana Phase 1 Operating Model](docs/02-hana-phase-1-operating-model.md)
- [Pricing, Batching & Settlement](docs/03-pricing-batching-settlement.md)
- [Courier, Trust, Zones & Capacity](docs/04-courier-zones-trust-capacity.md)
- [Zone Model v1](docs/30-zone-model-v1.md)
- [Courier Zone Approval v1](docs/31-courier-zone-approval-v1.md)
- [Zone Capacity v1](docs/32-zone-capacity-v1.md)
- [Hana MVP Capacity Rule v1](docs/33-hana-mvp-capacity-rule-v1.md)
- [Distance Feasibility Criterion v1](docs/34-distance-feasibility-criterion-v1.md)
- [Feasibility Evidence Gate v1](docs/35-feasibility-evidence-gate-v1.md)
- [Eligible Modes Feasibility Criterion v1](docs/36-eligible-modes-feasibility-criterion-v1.md)
- [Eligible Courier Feasibility Criterion v1](docs/37-eligible-courier-feasibility-criterion-v1.md)
- [Effective Zone Capacity Criterion v1](docs/38-effective-zone-capacity-criterion-v1.md)
- [Active Route Inventory Evidence v1](docs/39-active-route-inventory-evidence-v1.md)
- [Route Insertion Feasibility Evidence v1](docs/40-route-insertion-feasibility-evidence-v1.md)
- [SLA Feasibility Criterion v1](docs/41-sla-feasibility-criterion-v1.md)
- [Feasibility Outcome Evaluation v1](docs/42-feasibility-outcome-evaluation-v1.md)
- [Promise Commit Contract v1](docs/43-promise-commit-contract-v1.md)
- [Atomic Promise Commit v1](docs/44-atomic-promise-commit-v1.md)
- [Promise Commit Orchestration v1](docs/45-promise-commit-orchestration-v1.md)
- [Promise Read API v1](docs/46-promise-read-api-v1.md)
- [Promise Version History API v1](docs/47-promise-version-history-api-v1.md)
- [Feasibility Evidence Integrity v1](docs/48-feasibility-evidence-integrity-v1.md)
- [Feasibility Evidence Source Verification v1](docs/49-feasibility-evidence-source-verification-v1.md)
- [Maps, GPS, Location & Routing](docs/05-maps-location-routing.md)
- [Hana Urban Delivery Configuration](docs/customers/hana/urban-delivery-configuration.md)
- [Unified B2B Policy & Capability Model](docs/06-b2b-policy-capability-model.md)
- [Policy Draft and Lifecycle API](docs/operations/policy-authoring-api.md)

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

- [Internal API Key Provisioning](docs/operations/api-key-provisioning.md)
- [Policy Draft Authoring API](docs/operations/policy-authoring-api.md)
- [Mono Policy Framework v1](docs/07-policy-framework.md)
- [Mono Policy Catalog v1](docs/08-policy-catalog-v1.md)
- [Mono Domain Model v1](docs/09-domain-model-v1.md)
- [Mono Service Boundaries v1](docs/10-service-boundaries-v1.md)
- [Mono Event Model v1](docs/11-event-model-v1.md)
- [Mono B2B API Contract v1](docs/12-api-contract-v1.md)
- [Mono Data Architecture v1](docs/13-data-architecture-v1.md)
- [Mono Runtime Decision Architecture v1](docs/14-runtime-decision-architecture-v1.md)
- [Mono Security Architecture v1](docs/15-security-architecture-v1.md)
- [Mono Observability Architecture v1](docs/16-observability-architecture-v1.md)
- [Mono Deployment & Infrastructure Architecture v1](docs/17-deployment-infrastructure-architecture-v1.md)
- [Mono Product Backlog v1](docs/18-product-backlog-v1.md)
- [Mono MVP Scope v1](docs/19-mvp-scope-v1.md)
- [Mono Epics Roadmap v1](docs/20-epics-roadmap-v1.md)
- [Mono Product Architecture v1](docs/21-product-architecture-v1.md)
- [Mono UX Flows v1](docs/22-ux-flows-v1.md)
- [Merchant Mobile Backlog v1](docs/23-merchant-mobile-backlog-v1.md)
- [Mono Technical Stack and Internal AI ADR v1](docs/24-technical-stack-and-internal-ai-adr-v1.md)
