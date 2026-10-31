# Figma panel parity audit

**Repository checkpoint:** `sprint-221-webhook-delivery-workbench` at `91e2a0010878f89843e1a96bf0579ba752a5c4c3`  
**Figma file:** `TsYT48l66ee34uvOoS9XI2`  
**Review date:** 2026-10-21

Purpose: trace each unique Figma screen to the current repository and distinguish a working UI from a backend/API capability, an external dependency, or an unresolved product rule.

## Status key

- **UI partial:** a related screen exists, but does not implement the complete Figma workflow.
- **API only:** relevant backend capability exists, but the corresponding user-facing panel is absent.
- **No UI found:** no corresponding application UI was found at this repository checkpoint.
- **Decision/provider:** implementation depends on an external provider or an explicit product/business rule.

An endpoint alone does not complete a panel. Figma sample data is not production evidence and must not become fabricated KPIs or records.

## Summary

| Panel | Unique designed screens | Current repository coverage |
|---|---:|---|
| Operations Console | 18 | API-backed workbenches cover dispatch, missions, couriers, capacity, SLA, fare review, issues, workers, webhook deliveries, policies and customer/branch onboarding. Several designed screens remain partial or have no matching API. Navigation and shell were aligned to Figma in the current panel-parity branch. |
| Onboarding Console | 6 | **UI partial:** API-backed customer and branch onboarding workbench covers customer queue, registration, activation, branch registration/editing and audit. Courier review, secure document verification and onboarding tracking remain absent. |
| Courier App | 16 | A hosted static interaction demo covers the designed mission states; it is not the authenticated courier product app and does not call the production API. Mission, presence, issue, and finance APIs exist in parts. |
| Merchant Dashboard | 15 | Delivery and branch-read flows exist. PR #258 adds navigation and setup screens for settings, access, billing and integrations, but unsupported operations remain unconnected until their APIs exist. |
| Merchant Mobile | 15 | A basic mobile-oriented web shell exists for home, deliveries, create, notifications and branch/account entry. The remaining designed profile, finance, support, preferences and security flows are not complete. Six system-state variants are separately designed. |
| **Total** | **70** | Plus 6 system-state variants and prototype duplicates. |

Merchant Mobile prototype copies are duplicates, not additional unique screens. The former incoming-call reference frame has been converted into the Mono Operations Console caller-context flow; it is counted as a screen, with telephony integration explicitly marked as an external dependency. The caller flow now covers recognized, unmatched, and multiple-active-order states.

## 1. Operations Console — 18 screens

| Figma node | Screen | Current implementation | Gap / follow-up |
|---|---|---|---|
| `72:4` | Operations console | **UI partial:** dispatch queue and API-backed operations workbenches. | Reconcile dashboard KPIs, alerts and regional capacity with authoritative API aggregates. Counts must remain page-scoped unless the API provides system-wide values. |
| `79:2` | Mission detail | **UI partial:** mission detail, timeline and read audit. | Compare every designed field, state, action and event. Mutations require backend authorization and audit. |
| `82:2` | Incoming call — recognized caller | **Design added:** automatic caller-number context with linked customer/order/Mission, Route, SLA, cancellation eligibility and open exception. | Requires telephony/VoIP integration and authorized caller-number matching; all displayed values remain sample data. |
| `604:2` | Incoming call — unmatched caller | **Design added:** unmatched state preserves the number and provides fallback search. | Requires telephony/VoIP integration; no customer or Mission may be inferred when there is no match. |
| `608:2` | Incoming call — multiple active orders | **Design added:** lists active matches and highlights the most relevant Mission with its context. | Relevance ranking and match results must come from authoritative customer/Mission records; telephony provider remains external. |
| `84:2` | Route control | **UI partial:** Operations now supports route-ID lookup, the persisted current plan and stop order, mission deadlines, and route-read audit through the authorized APIs. | No route directory/list endpoint, map/GPS/ETA or network-wide capacity/risk summaries are available. Route creation/insertion orchestration is not exposed as an operations workflow in this panel. |
| `87:2` | Courier control | **UI partial:** presence roster and courier profiles. | Live GPS/map, shifts, vehicle, battery and control actions are not provided by presence snapshots. Confirm provider and allowed controls. |
| `89:2` | Zones and capacity | **UI partial:** planned capacity projections and availability windows. | Micro-zone map, geometry editing, demand/effective supply and risk visualization are not complete equivalents. Define authoritative data and map provider. |
| `95:2` | SLA exceptions | **UI partial:** SLA alert feed and structured courier issue inbox. | Match incident grouping, evidence, severity, recommendations and actions to explicit API contracts and policy. |
| `98:2` | Customers | **UI partial:** customer/branch onboarding plus administrator-scoped API-key metadata (ID, scopes and lifecycle timestamps) in the customer detail view. | Customer order/SLA metrics, delivery configuration, webhook health and associated missions lack this panel's API contract. API-key issuance remains outside the panel; revocation is available in Integrations. No secret key material is exposed. |
| `607:2` | Policies and configurations | **Design added:** policy catalog/version lifecycle, effective-policy preview and bounded HARD evaluation. | Policy authoring/effective/evaluate APIs exist, but previews do not apply policy to delivery requests or Missions. Activation/retirement need Mono-admin MFA, reason and audit; connect real API states before production use. |
| `607:353` | Audit operations | **UI partial:** a domain selector displays authorized Mission, Courier, Route-plan and Webhook audit APIs with each source's supported filters and pagination. | The current audit APIs primarily record privileged reads and webhook actions. A unified event stream for manual dispatch decisions, policy evaluations, settlement, before/after state and correlation-chain evidence remains unavailable. |
| `100:2` | Finance | **UI partial:** read-only mission fare review from recorded quote snapshots. | Actual ledger, collection, VAT remittance, invoices, payouts, wallet, adjustments and withdrawals are absent. Requires approved financial contracts and authorized provider/custodian. |
| `102:2` | Integrations | **UI partial:** webhook delivery operations/audit, per-customer API-key metadata/audit and audited key revocation, plus customer-side endpoint registration, lifecycle history, disablement and secret rotation. New signing secrets are revealed once in the connected Merchant Portal session. | Endpoint management requires a customer API key with `webhooks.manage`; delivery-health linkage by customer remains absent. API-key issuance remains an operator CLI workflow. |
| `104:2` | Tracking | **UI partial:** mission directory, exact detail and timeline. | Map/live tracking needs verified location events and provider; presence is not live GPS. |
| `106:2` | Overview | **UI partial / mapping unclear:** data is distributed across workbenches. | Decide if this is a distinct cross-domain overview or overlaps `72:4`; define source-backed KPIs. |
| `108:2` | Vehicles | **No UI found.** | Confirm scope and whether a vehicle API/domain is required. |
| `114:2` | Support and complaints | **UI partial:** structured courier issue inbox only. | Customer support ticket lifecycle, assignment, communications, resolution and audit are not equivalent to the issue inbox. Define the workflow and data boundaries. |

## 2. Onboarding Console — 6 screens

| Figma node | Screen | Current implementation | Gap / follow-up |
|---|---|---|---|
| `130:3` | Overview | **UI partial:** customer onboarding queue with status/search and pagination. | Cross-entity rollups for customer, courier and document queues are not available; do not imply totals beyond the current API page. |
| `153:2` | Couriers | **API only.** | Courier identity, application and status APIs exist in parts; onboarding queue/review UI is absent. |
| `157:2` | Businesses | **UI partial:** create Pending customer, inspect current status/audit, and activate with a required reason. Branches can be registered/edited for active customers. | API credentials and service configuration remain separate workflows; no business document review or full activation checklist is implied. |
| `161:2` | Verification documents | **No UI found.** | Requires secure storage, reviewer permissions, retention and access audit. Upload is not verification. |
| `162:2` | Activation | **UI partial:** reasoned activation for a Pending customer through the authoritative administrator/MFA API. | Courier activation and any eligibility/document decisions remain outside this panel; never infer approval from a document or status. |
| `163:2` | Tracking | **No matching onboarding UI found.** | Clarify whether this means onboarding cases, deliveries or courier activity; the label overlaps other tracking concepts. |

## 3. Courier App — 16 screens

| Figma node | Screen | Current implementation | Gap / follow-up |
|---|---|---|---|
| `169:3` | Home | **No courier app UI.** | App shell and authenticated courier session are absent. |
| `171:2` | En route to pickup | **No app UI; mission APIs exist.** | Navigation/location needs provider and policy decisions. |
| `173:2` | Arrived at pickup | **No app UI; lifecycle API exists.** | Confirm arrival transition and evidence contract. |
| `175:2` | En route to dropoff | **No app UI.** | GPS/navigation and mission state are not a mobile workflow today. |
| `178:2` | Final delivery | **No app UI; completion API exists.** | Confirm proof-of-delivery and recipient confirmation rules. |
| `180:2` | Mission complete | **No app UI.** | Show only server-confirmed completion and recorded earnings. |
| `182:2` | Missions history | **No app UI; history APIs exist.** | Build courier-scoped, authorized history with pagination. |
| `184:2` | Earnings wallet | **No app UI; finance APIs exist in parts.** | Establish ledger/payout semantics and authorized payment/custody provider before showing balances or payment status. |
| `186:2` | Account | **No app UI.** | Profile, session, identity and security controls are absent. |
| `188:2` | Recipient unavailable | **No app UI; issue/lifecycle APIs exist in parts.** | Define reason, retry, contact and escalation rules. |
| `189:2` | Order not ready | **No app UI.** | Define evidence, waiting period and mission effects. |
| `190:2` | Delivery incident | **No app UI; issue reporting API exists.** | Connect supported structured issue types; preserve safety escalation boundaries. |
| `192:2` | Incident registered | **No app UI.** | Display server acknowledgment and durable report reference. |
| `193:2` | Returning to origin | **No app UI; return lifecycle APIs exist in parts.** | Confirm who authorizes return and the state/evidence sequence. |
| `195:2` | Return handoff | **No app UI.** | Define receiver identity, custody and proof requirements. |
| `198:2` | Return complete | **No app UI.** | Show only server-confirmed return completion. |

## 4. Merchant Dashboard — 15 screens

| Figma node | Screen | Current implementation | Gap / follow-up |
|---|---|---|---|
| `207:3` | Dashboard | **UI partial:** live delivery/branch portal. | Compare summary cards with source-backed aggregates; current counts are scoped to returned pages. |
| `212:2` | Create delivery | **UI partial:** request creation exists. | Feasibility/quote requires verified coordinates and authoritative API. Never fabricate quote or promise. |
| `214:2` | Delivery detail and tracking | **UI partial:** exact-ID details and recorded tracking data. | Compare event timeline, help actions and map behavior; provider remains a dependency. |
| `216:2` | Deliveries list | **UI present:** paginated customer-scoped list. | Verify filters, sorting and state coverage against Figma. |
| `217:2` | Tracking | **UI partial:** tracking through delivery details. | Separate map/search and live location are absent; provider and event freshness are unresolved. |
| `225:2` | Branding settings | **No UI found.** | Needs settings persistence, validation and permissions. |
| `229:2` | Branches | **UI partial:** active branches are read. | Branch management/editing workflow is absent. |
| `236:2` | Billing | **No UI found.** | Needs real invoicing/payment/ledger contracts and an authorized provider/custodian. |
| `237:2` | Workspace settings | **No UI found.** | Define workspace ownership, persistence and permissions. |
| `238:2` | Members and access | **No UI found.** | Needs identity provider, roles, invitations, revocation and session policies. |
| `239:2` | Notification settings | **No UI found.** | Needs supported channels, preference and delivery APIs. |
| `241:2` | Integration settings | **UI partial:** connects the current customer key and opens endpoint management. | No separate user identity or member-scoped roles exist; API key scope currently gates endpoint access. Delivery health is not surfaced in settings. |
| `243:2` | Add branch | **No UI found.** | Confirm branch API create/update, validation, authorization and idempotency. |
| `244:2` | Invite member | **No UI found.** | Depends on the access flow and configured identity/email provider. |
| `245:2` | Add webhook endpoint | **UI present:** create HTTPS endpoint, choose supported events, list/inspect lifecycle audit, disable endpoints and rotate signing secret through customer-scoped APIs. | Verify all Figma validation and confirmation states in visual QA. A signing secret is shown once. |

## 5. Merchant Mobile — 15 unique screens

The Figma board also includes 6 system-state variants (empty, loading, error, infeasible, cancel confirmation, success), 15 prototype duplicates and a QA summary. Those are not extra functional screens.

| Figma sheet | Screen | Current implementation | Gap / follow-up |
|---|---|---|---|
| `358:3` | Home | **No merchant mobile UI.** | Mobile shell and session are absent. |
| `358:3` | Deliveries | **No mobile UI; web APIs exist.** | Build mobile navigation/list states against customer-scoped APIs. |
| `358:3` | Delivery detail | **No mobile UI; web detail exists.** | Match detail/tracking without inventing location or delivery evidence. |
| `358:3` | Create delivery | **No mobile UI; web form exists.** | Reuse verified quote/feasibility boundaries and idempotent creation. |
| `358:3` | Review and confirm | **No mobile UI.** | Define confirmation/retry behavior under uncertain network outcomes. |
| `361:2` | Notifications | **No mobile UI.** | Notification delivery and preference APIs are not present in the mobile app. |
| `361:2` | Exception | **No mobile UI.** | Define merchant-actionable exceptions and corresponding APIs. |
| `361:2` | Branch switch | **No mobile UI.** | Confirm multi-branch authorization and explicit scope switching. |
| `361:2` | Finance | **No mobile UI.** | Billing/ledger/provider dependencies apply. |
| `361:2` | Support | **No mobile UI.** | Customer support workflow/API is absent. |
| `362:2` | More | **No mobile UI.** | Account/navigation shell is absent. |
| `362:2` | Profile | **No mobile UI.** | Requires identity/profile API and access controls. |
| `362:2` | Settings | **No mobile UI.** | Workspace settings and persistence are absent. |
| `362:2` | Notification settings | **No mobile UI.** | Notification preference API is absent. |
| `362:2` | Security | **No mobile UI.** | Depends on identity provider, session lifecycle and security policy. |

## Product-document baseline checked in this review

- `docs/19-mvp-scope-v1.md` — MVP scope and urban delivery operating limits.
- `docs/21-product-architecture-v1.md` — five product surfaces and Operations information architecture.
- `docs/22-ux-flows-v1.md` and `docs/23-merchant-mobile-backlog-v1.md` — required flows, mobile priorities and web-first boundaries.
- `docs/06-b2b-policy-capability-model.md`, `docs/08-policy-catalog-v1.md`, and `docs/operations/policy-authoring-api.md` — unified policy model, MVP policy families, API lifecycle, preview limits, MFA and audit requirements.

Figma was updated to add the distinct Merchant Mobile surface to the product map; mark sample data consistently on supporting boards; convert the incoming-call reference into recognized, unmatched and multiple-order Operations states; map Operations navigation to Policies/Configurations, SLA/Exceptions and Audit; add policy-authoring and audit concepts; and remove the false claim that the displayed Hana customer record is live. The current policy-authoring API explicitly does not apply activated policies to delivery requests or Missions, so the design labels its previews as non-executing.

## Out-of-design additions to reconcile

The current Operations Console includes worker status, courier issue inbox, courier availability scheduling/cancellation and webhook delivery replay/cancellation. These tools do not each have a matching Figma screen. Place them in the information architecture and review roles/labels. The Operations Console README calls the console “read-only” although availability and webhook actions can mutate state; reconcile this wording with actual behavior.

## Recommended execution order

1. **Close design-to-API contracts:** source-backed overview metrics, mission/SLA fields and actions, route/zone semantics, and the two Operations overviews.
2. **Complete existing web panels:** merchant branches/settings/access and onboarding, after identity, permission and API contracts are ready.
3. **Build mobile products:** courier and merchant mobile shells/session lifecycle, then their Figma flows using existing APIs and explicit state handling.
4. **Defer provider/policy-gated screens:** live maps/GPS, billing/settlement/payouts, sensitive document verification and support until decisions are recorded.
5. **Reconcile code-only operations tools:** Figma placement for worker status, issue inbox, availability controls and webhook workbench; correct read-only/action labels.

## Per-screen completion criteria

A screen is complete only when designed states/actions are accounted for; its API is authoritative and role-checked; loading, empty, error and permission-denied states are covered; pagination/counts are honest; sensitive fields are minimized; mutations are idempotent and audited where required; and no placeholder data is presented as live data. Track each blocked screen with the exact missing API, provider or decision.

## Review boundary

This is a Figma/repository inventory, not pixel-level visual QA or release approval. It does not certify every Figma interaction/variant, and it does not approve external provider, financial, identity or document-verification choices. No Stage or Production claim is made.

## Panel synchronization update — 2026-10-31

- The Operations Console shell now uses the Figma teal navigation rail, matching desktop width/background, the Figma panel names and a page-specific heading/description for every implemented view. Existing useful operational workbenches remain reachable under the corresponding product areas: dispatch, missions, routes, courier presence, SLA, support, capacity, standards, onboarding, finance and integrations.
- Route control now reads an administrator-authorized saved plan by route ID, renders its ordered pickup/drop-off stops and committed delivery times, and shows scoped append-only read audit. It does not invent the Figma sample route list, map, location, ETA, capacity or risk values.
- The Audit panel now reads the existing Mission, Courier, Route-plan and Webhook audit endpoints. Source selection and filters follow each API's real contract; the screen does not claim to search all operational events.
- These labels do not imply that every Figma screen is complete. Route directory/map and route planning actions, live GPS tracking, vehicle inventory, a unified cross-domain operational-event search, and telephony caller context still require their own authoritative APIs/providers or additional UI. Customer and policy audit views remain scoped to their current domain workbenches.
- The Onboarding Console remains a set of API-backed admissions and customer/branch workbenches inside Operations. Its Figma concepts for courier document review and onboarding tracking cannot be completed safely with the current API contracts.
- Merchant Dashboard settings navigation is available for review in [PR #258](https://github.com/mahdimarzooghi4-debug/mono-logistic/pull/258). Webhook endpoint management now uses the existing customer-scoped API; billing, workspace persistence, members, notification preferences and branch creation remain unconnected.
- The mobile shell and courier demo are separate review artifacts. The merchant mobile shell has only its currently implemented views; the courier demo uses sample data and local interactions. Neither completes the corresponding authenticated mobile product.

The Operations Console TypeScript/Vite production build and its 19 tests passed after the shell, route-plan and domain-scoped audit workbench updates. This is a code/build check, not a pixel-level verification of every Figma frame.

## Implementation update — 2026-10-21

The Operations Console now includes an API-backed customer/branch onboarding workbench. It covers the customer queue, Pending customer registration, reasoned customer activation, active-customer branch registration/editing, and customer/branch audit inspection. API-key metadata and per-key audit are available in the Integrations panel and customer detail view. Customer creation does not issue API credentials. Document verification, courier onboarding review, and cross-entity onboarding analytics remain gaps.

## Customer panel sync — 2026-10-31

The Operations customer detail view now also reads the administrator-scoped API-key inventory and shows only key ID, scopes, creation/expiry and revocation timestamps. It never displays secret key material. The API does not expose customer order/SLA KPIs, delivery configuration, webhook health, or customer-associated missions in this panel, so those Figma details remain explicitly unimplemented. Credential issuance remains an operator CLI workflow; revocation is available from Integrations with an audited reason. Customer activation does not create credentials.
