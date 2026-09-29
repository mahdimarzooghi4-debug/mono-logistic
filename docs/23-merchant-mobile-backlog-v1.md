# Mono Merchant Mobile Backlog v1

## 1. Goal

Turn the approved Merchant Mobile IA, UX flows, Figma screens, prototype and system states into buildable product work.

Merchant Mobile is a distinct product surface for day-to-day merchant operations.

It is not a responsive copy of Merchant Dashboard.

Primary product outcome:

> A merchant can create, monitor and act on deliveries from mobile without losing operational truth, branch context or delivery-promise clarity.

## 2. Design Sources

Figma file:
- `TsYT48l66ee34uvOoS9XI2`

Approved Merchant Mobile boards:
- Critical Flows v1: `358:3`
- Supporting Flows v1: `361:2`
- Account & Settings v1: `362:2`
- End-to-End Prototype start: `369:2`
- QA Summary v1: `369:707`
- System States v1: `374:2`

Architecture / UX source:
- `docs/21-product-architecture-v1.md`
- `docs/22-ux-flows-v1.md`

## 3. Backlog Structure

```
Epic
  -> Capability
  -> User Story
  -> Acceptance Criteria
  -> Technical Tasks
  -> Test Evidence
```

Priority:
- P0 = required for Merchant Mobile first production release
- P1 = required shortly after first release / can ship behind feature flag
- P2 = deferred enhancement

## 4. Epic MM1 — Mobile Foundation & Authentication

Priority: P0

### MM1.1 App Shell

User Story:
As a merchant user, I want a stable mobile application shell so I can access daily logistics operations reliably.

Acceptance Criteria:
- Persian RTL is the default interface direction.
- Vazirmatn is used consistently.
- Bottom navigation contains Home, Deliveries, Create, Notifications and More.
- Current tab is visually explicit.
- Navigation state survives normal foreground/background transitions.
- No desktop sidebar/table pattern is reused on mobile.

### MM1.2 Authentication Session

User Story:
As an authorized merchant user, I want to securely sign in and stay signed in according to policy.

Acceptance Criteria:
- Authentication uses the shared Mono identity / tenant model.
- User is resolved to customer/workspace and allowed branches.
- Expired or revoked session returns the user to authentication safely.
- Tenant context cannot be changed by client-side parameter manipulation.
- Logout clears local authenticated state.
- Session-related failures do not expose sensitive data.

### MM1.3 Bootstrap Context

Acceptance Criteria:
- App startup resolves current user, customer/workspace, permissions and branch context.
- App can distinguish loading, authenticated, unauthorized and recoverable error states.
- Initial data load does not block the entire app if non-critical modules fail.

## 5. Epic MM2 — Home & Attention Queue

Priority: P0

### MM2.1 Operational Home

User Story:
As a merchant operator, I want Home to show what needs attention now so I can act without scanning reports.

Acceptance Criteria:
- Current branch/workspace context is visible.
- Active delivery count is visible.
- Deliveries requiring attention are prioritized above routine activity.
- Recent deliveries show status and delivery promise context.
- Home includes a primary Create Delivery action.
- Analytics do not displace operational attention.

### MM2.2 Risk / Exception Entry

Acceptance Criteria:
- A delivery at risk is visually distinct without excessive alarm styling.
- Tapping the risk item opens the exact delivery / exception context.
- Committed promise and current ETA are not conflated.
- If no action is required from merchant, the UI states what Mono is doing.

### MM2.3 Home Empty State

Acceptance Criteria:
- Empty state explains that there are no active deliveries.
- Primary next action is Create Delivery.
- Empty state does not look like a system error.

## 6. Epic MM3 — Deliveries & Delivery Detail

Priority: P0

### MM3.1 Delivery List

User Story:
As a merchant operator, I want to find active and historical deliveries quickly.

Acceptance Criteria:
- Active, completed and failed/cancelled states are separated.
- Search supports at minimum customer order reference and recipient.
- List item shows status, destination/recipient context and promise/status summary.
- Risk deliveries are distinguishable from healthy active deliveries.
- Access is tenant- and branch-scoped by authorization.

### MM3.2 Delivery Detail

User Story:
As a merchant operator, I want one authoritative delivery view so I know what was promised, what is happening now and what I can do.

Acceptance Criteria:
- Shows current Mission state.
- Shows committed delivery promise.
- Shows current ETA separately from committed promise.
- Shows pickup branch and destination.
- Shows latest event and timeline.
- Shows courier / route context only where customer-visible.
- Shows cancellation eligibility.
- Shows active exception / incident if any.
- Only actions allowed by current policy are exposed.

### MM3.3 Delivery Timeline

Acceptance Criteria:
- Events are chronologically ordered.
- Event timestamps are authoritative server timestamps where applicable.
- Important events include assignment, pickup/handoff, in-transit, SLA risk, delivered/failed/cancelled.
- Timeline does not invent client-only logistics state.

## 7. Epic MM4 — Create Delivery, Feasibility, Quote & Promise

Priority: P0

### MM4.1 Create Delivery Form

User Story:
As a merchant operator, I want to create a delivery quickly from my current branch.

Acceptance Criteria:
- Current branch is preselected when authorized.
- User can change pickup branch if permitted.
- Recipient name and phone are captured.
- Destination is captured as address + coordinates where supported.
- Shipment facts required by the selected service are captured.
- Ready time is captured where applicable.
- Form progress is preserved through recoverable errors.

### MM4.2 Feasibility Request

Acceptance Criteria:
- Submission calls the same feasibility rules used by API / web.
- Loading state explicitly says feasibility / delivery time is being checked.
- Duplicate taps do not create duplicate requests.
- Request is idempotent where a write is involved.

### MM4.3 Feasible Quote

Acceptance Criteria:
- Fare is shown before final confirmation.
- Committed delivery promise is clearly labeled.
- Pickup, destination, recipient and shipment summary are shown.
- User must explicitly confirm before Mission creation.
- An estimate must never be presented as a committed promise.

### MM4.4 No Feasibility

Acceptance Criteria:
- UI states that Mono cannot currently commit the delivery.
- Customer-visible rejection reason is shown.
- Entered form data is preserved.
- User can change applicable input such as ready time, origin or destination and retry.
- No Mission is created.
- No delivery promise is shown.

### MM4.5 Creation Success

Acceptance Criteria:
- Success state shows created order / Mission reference.
- Committed promise is shown.
- Assignment state is shown without pretending a courier exists before assignment.
- User can open Delivery Detail or create another delivery.

## 8. Epic MM5 — Notifications, Exceptions & Merchant Actions

Priority: P0

### MM5.1 Notification Inbox

User Story:
As a merchant operator, I want operational notifications prioritized so important changes are not buried.

Acceptance Criteria:
- Notification categories include exception/risk, assignment, pickup, delivery, cancellation/resolution and finance notices.
- Exception / SLA-risk items are prioritized.
- Read/unread state is supported.
- Notification content is scoped to authorized customer / branch data.

### MM5.2 Deep Link to Context

Acceptance Criteria:
- Tapping an operational notification opens the exact related delivery / exception.
- User is not dropped into a generic list when exact context exists.
- Missing / inaccessible target is handled safely with an explanatory state.

### MM5.3 Exception Context

Acceptance Criteria:
- Shows what happened.
- Shows impact on committed promise.
- Shows customer-visible reason where allowed.
- Shows Mono's current action / next step.
- Shows merchant action only when one is actually allowed.
- Support entry carries delivery context automatically.

## 9. Epic MM6 — Cancellation

Priority: P0

### MM6.1 Cancellation Eligibility

Acceptance Criteria:
- Eligibility is resolved from shared cancellation policy and authoritative handoff state.
- UI must not independently infer eligibility from local state.
- Before handoff, cancellation may be offered when policy permits.
- At/after authoritative handoff, cancellation is blocked if policy says so.

### MM6.2 Cancellation Confirmation

Acceptance Criteria:
- Confirmation shows consequences before destructive action.
- Financial consequence / fee is shown if applicable and customer-visible.
- User must explicitly confirm.
- Successful cancellation updates Mission state and timeline.
- Duplicate confirmation is safe / idempotent.
- If policy changes before confirmation, server response wins and UI refreshes.

### MM6.3 Cancellation Blocked

Acceptance Criteria:
- Reason is shown.
- Delivery context remains available.
- Support / incident route is offered where relevant.

## 10. Epic MM7 — Branch Context

Priority: P0

### MM7.1 Branch Switcher

User Story:
As a multi-branch merchant user, I want to switch authorized branches without accidentally operating on the wrong origin.

Acceptance Criteria:
- Active branch is visible in operational screens.
- Only authorized branches appear.
- Branch status / readiness is visible where useful.
- Selecting a branch refreshes scoped Home / deliveries.
- Create Delivery defaults to active branch.
- Inactive branch cannot create delivery if configuration forbids it.

### MM7.2 Branch Context Persistence

Acceptance Criteria:
- Selected branch persists appropriately across app navigation.
- Workspace/customer switch cannot leak data from prior context.
- Deep links resolve authorization before opening branch-scoped data.

## 11. Epic MM8 — Finance Summary

Priority: P1

### MM8.1 Mobile Finance Overview

User Story:
As a merchant manager, I want lightweight financial visibility on mobile.

Acceptance Criteria:
- Current billing summary is shown.
- Recent delivery charges are shown.
- Settlement / outstanding status is shown where applicable.
- Values come from authoritative finance / billing projection.
- Mobile does not attempt full reconciliation workflows.

### MM8.2 Web Handoff

Acceptance Criteria:
- Deep billing / invoice / reconciliation administration remains web-first.
- Mobile communicates this boundary clearly.

## 12. Epic MM9 — Support

Priority: P0

### MM9.1 Contextual Support

Acceptance Criteria:
- Entering support from Delivery Detail attaches delivery / Mission context automatically.
- User does not re-enter known identifiers.
- User can select issue category.
- Support request captures current customer / branch / delivery context.
- Submission success and failure states are explicit.

### MM9.2 General Support

Acceptance Criteria:
- Support is accessible from More without a delivery context.
- Delivery-specific fields are not required for a general support request.

## 13. Epic MM10 — Profile, Preferences & Security

Priority: P1

### MM10.1 Profile

Acceptance Criteria:
- Shows user identity, role, business and default branch.
- Editability follows RBAC.
- Sensitive account fields are not exposed to unauthorized roles.

### MM10.2 Notification Preferences

Acceptance Criteria:
- User can control non-mandatory operational / finance notifications.
- Critical operational notifications follow platform/customer policy and cannot be silently disabled if policy requires delivery.
- Preference changes persist server-side where cross-device behavior is required.

### MM10.3 Security & Sessions

Acceptance Criteria:
- Current device/session is visible.
- Other active sessions can be listed where identity service supports it.
- Password change entry is available if password auth is used.
- User can sign out current session.
- Sign-out-all action requires confirmation and server authorization.

### MM10.4 Web-first Administration Boundary

Acceptance Criteria:
Mobile does not own:
- organization-level members / access administration
- integrations / webhook configuration
- visual identity / co-branding configuration
- advanced workspace policy/configuration
- deep accounting / reconciliation
- bulk administration

## 14. Epic MM11 — System States & Resilience

Priority: P0

### MM11.1 Loading

Acceptance Criteria:
- Loading state says what the system is doing.
- Blocking load is used only when next action genuinely depends on the result.
- App does not present stale promise as current result.

### MM11.2 Retryable Error

Acceptance Criteria:
- User-entered form data is preserved.
- Error distinguishes temporary technical failure from business rejection.
- Retry action is available when safe.
- Repeated retry remains idempotent.

### MM11.3 Empty

Acceptance Criteria:
- Empty state is contextual and actionable.
- Empty state is not styled as error.

### MM11.4 Success

Acceptance Criteria:
- Success confirms authoritative server result.
- Shows next logical action.
- Does not imply assignment / delivery state that has not happened.

## 15. Epic MM12 — Mobile Platform Quality

Priority: P0

### MM12.1 Accessibility / Interaction

Acceptance Criteria:
- Critical touch targets meet practical mobile sizing.
- Text remains readable at supported system scaling.
- Critical meaning is not encoded by color alone.
- RTL order remains correct across navigation, lists, forms and timelines.

### MM12.2 Performance

Acceptance Criteria:
- App shell becomes interactive without waiting for every secondary dataset.
- List pagination / incremental loading is supported.
- Images/assets do not block logistics state.
- Network timeout / retry behavior is defined.

### MM12.3 Observability

Acceptance Criteria:
- Mobile API failures include correlation ID where supported.
- Key flow metrics are observable:
  - login/bootstrap failure
  - create delivery started/completed/failed
  - feasibility accepted/rejected/error
  - delivery detail load failure
  - notification deep-link failure
  - cancellation request/result
- Client telemetry must not include prohibited sensitive payloads.

### MM12.4 Security

Acceptance Criteria:
- No cross-tenant cache leakage.
- Tokens / secrets use platform-secure storage.
- Sensitive data is not written to insecure logs.
- Authorization is enforced server-side for every protected action.

## 16. Release Slice

### Merchant Mobile Release 1 — P0

Required:
- MM1 Foundation & Authentication
- MM2 Home & Attention
- MM3 Deliveries & Detail
- MM4 Create / Feasibility / Quote / Promise
- MM5 Notifications & Exceptions
- MM6 Cancellation
- MM7 Branch Context
- MM9 Support
- MM11 System States
- MM12 Platform Quality

### Release 1.1 — P1

- MM8 Finance Summary
- MM10 Profile / Preferences / Security refinements

## 17. Sprint Candidate 1

Goal:

> A merchant user can authenticate, see branch-scoped operational Home, browse active deliveries and open authoritative Delivery Detail.

Stories:
- MM1.1 App Shell
- MM1.2 Authentication Session
- MM1.3 Bootstrap Context
- MM2.1 Operational Home
- MM2.2 Risk / Exception Entry
- MM2.3 Home Empty State
- MM3.1 Delivery List
- MM3.2 Delivery Detail
- MM3.3 Delivery Timeline
- MM7.1 Branch Switcher foundation
- MM12.1 Accessibility / Interaction baseline
- MM12.3 Observability baseline
- MM12.4 Security baseline

Sprint exit:
- authenticated merchant sees only authorized tenant/branch data
- Home loads operational state
- Deliveries list loads
- Delivery Detail distinguishes Promise vs ETA
- branch switch updates context safely
- basic error / empty / loading states exist
- telemetry and authorization tests exist

## 18. Sprint Candidate 2

Goal:

> A merchant can create a delivery end-to-end with feasibility, fare and committed promise.

Stories:
- MM4.1 Create Delivery Form
- MM4.2 Feasibility Request
- MM4.3 Feasible Quote
- MM4.4 No Feasibility
- MM4.5 Creation Success
- MM11.1 Loading
- MM11.2 Retryable Error
- MM11.4 Success

Sprint exit:
- form survives retryable failure
- accepted flow creates one Mission only
- rejected feasibility creates no Mission
- fare and committed promise are explicit
- idempotency tests pass

## 19. Sprint Candidate 3

Goal:

> Merchant receives operational changes, opens exact context and takes allowed action.

Stories:
- MM5.1 Notification Inbox
- MM5.2 Deep Link to Context
- MM5.3 Exception Context
- MM6.1 Cancellation Eligibility
- MM6.2 Cancellation Confirmation
- MM6.3 Cancellation Blocked
- MM9.1 Contextual Support
- MM9.2 General Support

Sprint exit:
- risk notification opens correct Mission
- cancellation obeys authoritative handoff/policy
- support receives context without re-entry
- destructive action confirmation is implemented
- audit / telemetry exists for merchant action

## 20. Definition of Ready

A Merchant Mobile story is Ready only when:
- business rule / policy source is identified
- Figma source state is identified
- API/application capability exists or dependency is explicit
- authorization scope is defined
- loading / empty / error behavior is defined
- acceptance criteria are testable
- analytics / audit requirements are identified where relevant

## 21. Definition of Done

A Merchant Mobile story is Done only when:
- implementation matches approved mobile UX intent
- Persian RTL QA passes
- unit / integration tests pass
- tenant/authorization tests pass
- error and empty states are handled
- observability is added
- accessibility / touch-target review passes
- Stage QA passes
- product acceptance passes

## 22. Traceability Rule

Every Merchant Mobile implementation item must trace:

```
Product Decision
-> Merchant Mobile UX Flow
-> Figma Screen / State
-> Epic / Story
-> Acceptance Criteria
-> API / Domain Capability
-> Code
-> Test
-> Stage QA
```
