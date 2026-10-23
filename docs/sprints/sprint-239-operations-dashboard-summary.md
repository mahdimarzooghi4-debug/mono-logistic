# Sprint 239: authoritative operations dashboard summary

## Goal

Implement the aggregate metrics shown on the Operations Console dashboard using current, authoritative API data.

## Delivered

- Added an administrator-only dashboard summary API with Tehran business-day semantics and an explicit SLA horizon.
- Aggregated active and unassigned missions, online couriers, distinct upcoming SLA risk and overdue counts, and delivered/on-time results for the business day.
- Audited summary reads and documented every field's source and calculation.
- Replaced dispatch-page-only KPI counts with system-wide summary values. Missing API data remains visibly unavailable.
- Kept zone capacity and incoming-call metrics outside this contract because their authoritative data and provider decisions are pending.

## Verification

- Operations Console tests and production build pass locally.
- API tests and compilation must run in CI; the local environment does not include the .NET SDK.
