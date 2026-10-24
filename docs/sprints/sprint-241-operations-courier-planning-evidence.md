# Sprint 241 — Operations courier planning evidence panel

## Goal

Show authoritative, audited courier planning and self-reported presence evidence in the existing Zones and Capacity workspace.

## Delivered

- Read the administrator-protected courier planning eligibility endpoint using the submitted zone and UTC time range.
- Show planned-capacity participation, reported online state, approved-zone presence candidate, last update time, and explicit exclusion reasons as separate facts.
- Keep summaries limited to the visible API page and preserve filter-bound cursor pagination.
- Abort stale requests, avoid browser caching, handle invalid/error/empty/loading states, and clear results when the operations token is removed.
- Keep the panel read-only and explicitly state that these signals do not prove physical presence, assignable capacity, or dispatch authority.

## Verification

- The Operations Console workflow runs component tests, TypeScript checking, and the production Vite build.
- Mono CI runs .NET build/tests, migration checks, API smoke tests, and the container image smoke test.

## Boundary

No GPS map, location history, dispatch authorization, Stage deployment, or Production activation is included.
