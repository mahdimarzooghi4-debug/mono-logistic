# Sprint 149 — Live-presence evidence in operations console

## Goal

Make the existing courier planning and self-reported online evidence available alongside planned zone capacity in the Mono operations console.

## Scope

- Read the administrator-protected `GET /operations/v1/courier-planning-eligibility` endpoint for the selected zone and time window.
- Show planned-capacity contribution separately from online status, approved-zone match, latest presence update time, and independent exclusion reasons.
- Keep the result keyset-paginated and page summaries limited to the displayed couriers.
- Document that online state is self-reported and that the view does not apply a freshness threshold.

## Boundary

This is read-only evidence. It does not establish physical presence, a dispatch candidate authorization, available capacity, assignment permission, or permission to send an offer. No GPS, coordinates, maps, or route tracking are introduced.

## Validation

The console must pass `npm run build` (TypeScript check and Vite production build). Repository CI additionally runs the .NET build/tests and container build on the exact PR head.
