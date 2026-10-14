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
- [Route Travel-Time Evidence v1](docs/61-route-travel-time-evidence-v1.md)
- [Route Insertion Sequencing Core (Sprint 150)](docs/sprints/sprint-150-route-sequencing-core.md)
- [Versioned Route Plan Storage (Sprint 151)](docs/sprints/sprint-151-route-versioned-storage.md)
- [Route Insertion Orchestration (Sprint 152)](docs/sprints/sprint-152-route-insertion-orchestration.md)
- [Route Matrix Evidence Verification (Sprint 153)](docs/sprints/sprint-153-route-matrix-evidence-verification.md)
- [SLA Feasibility Criterion v1](docs/41-sla-feasibility-criterion-v1.md)
- [Feasibility Outcome Evaluation v1](docs/42-feasibility-outcome-evaluation-v1.md)
- [Promise Commit Contract v1](docs/43-promise-commit-contract-v1.md)
- [Atomic Promise Commit v1](docs/44-atomic-promise-commit-v1.md)
- [Promise Commit Orchestration v1](docs/45-promise-commit-orchestration-v1.md)
- [Promise Read API v1](docs/46-promise-read-api-v1.md)
- [Promise Version History API v1](docs/47-promise-version-history-api-v1.md)
- [Feasibility Evidence Integrity v1](docs/48-feasibility-evidence-integrity-v1.md)
- [Feasibility Evidence Source Verification v1](docs/49-feasibility-evidence-source-verification-v1.md)
- [Hana Distance Pricing v1](docs/50-hana-distance-pricing-v1.md)
- [Versioned Quote Contract v1](docs/51-versioned-quote-contract-v1.md)
- [Quote Persistence v1](docs/52-quote-persistence-v1.md)
- [Quote Read API v1](docs/53-quote-read-api-v1.md)
- [Hana Distance Pricing Evidence v1](docs/54-hana-distance-pricing-evidence-v1.md)
- [Quote Issuance Core v1](docs/55-quote-issuance-core-v1.md)
- [Quote Issuance API v1](docs/56-quote-issuance-api-v1.md)
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
- [Dispatch Operations API](docs/operations/dispatch-api.md)
- [Sprint 133 — Dispatch deadline filter](docs/sprints/sprint-133-dispatch-deadline-filter.md)
- [Sprint 134 — Batch overdue offer expiry](docs/sprints/sprint-134-batched-overdue-expiry.md)
- [Sprint 135 — Batched dispatch offers](docs/sprints/sprint-135-batched-dispatch-offers.md)
- [Sprint 136 — Dispatch outcome filter](docs/sprints/sprint-136-dispatch-outcome-filter.md)
- [Sprint 137 — Dispatch delivery deadline filter](docs/sprints/sprint-137-dispatch-delivery-deadline-filter.md)
- [Sprint 138 — Dispatch attempt count filters](docs/sprints/sprint-138-dispatch-attempt-count-filters.md)
- [Sprint 139 — Dispatch attempt recency filter](docs/sprints/sprint-139-dispatch-attempt-recency-filter.md)
- [Sprint 140 — Dispatch delivery overdue filter](docs/sprints/sprint-140-dispatch-delivery-overdue-filter.md)
- [Sprint 141 — Dispatch delivery due sorting](docs/sprints/sprint-141-dispatch-delivery-due-sort.md)
- [Sprint 142 — Dispatch delivery window filter](docs/sprints/sprint-142-dispatch-delivery-window-filter.md)
- [Sprint 143 — Dispatch delivery window state](docs/sprints/sprint-143-dispatch-delivery-window-state.md)
- [Sprint 144 — Dispatch delivery window state filter](docs/sprints/sprint-144-dispatch-delivery-window-state-filter.md)
- [Sprint 145 — Dispatch due horizon](docs/sprints/sprint-145-dispatch-due-horizon.md)
- [Sprint 146 — Dispatch customer filter](docs/sprints/sprint-146-dispatch-customer-filter.md)
- [Sprint 147 — Dispatch waiting cutoff](docs/sprints/sprint-147-dispatch-waiting-cutoff.md)
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
- [Sprint 115 — Explainable courier planning eligibility](docs/sprints/sprint-115-courier-planning-eligibility-preview.md)
- [Sprint 116 — Courier authentication boundary](docs/sprints/sprint-116-courier-authentication-boundary.md)
- [Sprint 117 — Mono-managed courier identity binding](docs/sprints/sprint-117-courier-identity-binding.md)
- [Sprint 118 — Courier live presence](docs/sprints/sprint-118-courier-live-presence.md)
- [Sprint 119 — Live presence in courier planning evidence](docs/sprints/sprint-119-live-presence-planning-gate.md)
- [Sprint 120 — Courier mission offers](docs/sprints/sprint-120-courier-dispatch-offers.md)
- [Sprint 121 — Operations courier offer queue](docs/sprints/sprint-121-dispatch-offer-operations-control.md)
- [Sprint 122 — Courier offer withdrawal](docs/sprints/sprint-122-courier-offer-withdrawal.md)
- [Sprint 123 — Suspension offer recovery](docs/sprints/sprint-123-suspension-offer-recovery.md)
- [Sprint 124 — Courier offer event history](docs/sprints/sprint-124-offer-event-history.md)
- [Sprint 125 — Courier mission execution](docs/sprints/sprint-125-courier-mission-execution.md)
- [Sprint 126 — Courier mission exceptions](docs/sprints/sprint-126-courier-mission-exceptions.md)
- [Sprint 127 — Pre-handoff courier reassignment](docs/sprints/sprint-127-prehandoff-reassignment.md)
- [Sprint 128 — Dispatch queue workbench](docs/sprints/sprint-128-dispatch-queue-workbench.md)
- [Sprint 129 — Courier offer expiry](docs/sprints/sprint-129-courier-offer-expiry.md)
- [Sprint 130 — Mission dispatch attempt history](docs/sprints/sprint-130-dispatch-attempt-history.md)
- [Sprint 131 — Dispatch queue attempt outcomes](docs/sprints/sprint-131-dispatch-queue-outcomes.md)
- [Sprint 132 — Overdue offer queue guidance](docs/sprints/sprint-132-expired-offer-queue-guidance.md)
