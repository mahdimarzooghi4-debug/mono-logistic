# Sprint 240: operations dashboard drilldowns

## Goal

Make dashboard KPIs actionable by taking operators to existing operational screens and applying supported filters.

## Delivered

- Turn online couriers into a keyboard-accessible action that opens the existing presence list filtered to `ONLINE`.
- Link unassigned missions to the dispatch queue.
- Link SLA risk to the existing alert list using the same horizon as the dashboard summary.
- Link active missions to the existing mission registry.
- Keep on-time delivery as a read-only KPI because no delivered-today drilldown filter exists in the current API.
- Add responsive focus/hover treatment and tests for values, unavailable states, and navigation callbacks.

## Verification

- Operations Console: 13 tests passed.
- TypeScript and Vite production build passed.
- git diff --check passed.
