# Sprint 224 — Operations customer and branch onboarding workbench

## Goal

Close the first internal UI gap in the Figma Onboarding Console and Merchant Dashboard branch-management flow by connecting the existing administrator/MFA APIs to the Operations Console.

## Product/API basis

- `docs/operations/customer-onboarding-api.md`
- `docs/operations/branch-onboarding-api.md`
- `docs/figma-panel-parity-audit.md`
- Figma panel-parity audit: Onboarding Console overview/businesses and Merchant Dashboard branches/add-branch.

## Delivered

- Paginated customer queue with PENDING/ACTIVE filters and prefix search.
- Customer registration as PENDING with required reason. The form does not create credentials.
- Explicit reasoned activation confirmation. The API remains authoritative and credential provisioning remains separate.
- Customer detail and append-only customer audit inspection.
- Active-customer branch registration and full editable-detail replacement with reason.
- Paired-coordinate validation, range validation, JSON-object validation for operating hours, and API-backed branch detail/audit inspection.
- Administrator/MFA denial, empty/loading/error, and stale selection handling without fixture rows.
- Figma parity audit and Operations Console README updated to reflect current coverage and residual gaps.

## Verification

- Operations Console `npm ci`, `npm run build`, `npm test`.
- Ten UI tests pass across policy and onboarding workbenches.
- `git diff --check`.

## Boundaries

This UI does not verify legal documents, approve couriers, issue API credentials, provision integrations, activate services, settle money, or decide eligibility. Cross-entity onboarding analytics and external identity/document providers remain out of scope. The work remains a Draft stacked on the previous Draft; no merge or Stage deployment is included.
