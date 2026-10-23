# Sprint 236 — Business Dashboard Alignment

## Goal

Bring the implemented business portal dashboard closer to Figma frame `207:3` while using only authenticated, current API data.

## Delivered

- Add dashboard quick actions for new delivery and quick tracking.
- Add a compact branch status list from the authenticated customer's active branch response.
- Add an urban-delivery service summary that points merchants to the request's authoritative quote and commitment.
- Keep dashboard metrics explicitly scoped to the currently loaded API page; do not present page counts as account totals.
- Add a responsive two-column dashboard that stacks on mobile.
- Add interaction coverage for quick tracking and branch/service widgets.

## Figma/API gaps retained

- Figma's all-account metrics (active, needs action, delivered today, average delivery duration) need a customer-scoped aggregate endpoint and agreed definitions for each metric.
- Figma's exception cards need an authenticated customer-facing issue/exception feed. Existing APIs do not provide one.
- Invoice, service configuration details, and account settings need supported merchant-facing APIs before those controls can be enabled.
- The mock Figma values and sample personal data are not shipped as live merchant data.

## Verification

- `npm test`: 2 tests passed.
- `npm run build`: passed.

## Boundary

No payment, invoice, pricing, delivery-time, or operational-alert values are inferred or mocked. No API credentials are persisted. No deployment, Stage, or Production change is included.
