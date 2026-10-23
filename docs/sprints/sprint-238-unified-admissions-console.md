# Sprint 238 — Unified Admissions Console

## Goal

Add the Figma `130:2` onboarding overview to the operations console without introducing a parallel admission state model or fabricated queue data.

## Implemented

- Added a unified, searchable overview for courier and business records.
- Reads the first page (up to 100 records) from the existing courier and customer operations APIs and reports per-source errors independently.
- Shows only page-scoped counts and status values returned by those APIs.
- Links each record type to its existing detailed operations workflow.
- Documents the courier lifecycle and the currently implemented business lifecycle in the UI.

## Deliberate scope limits

- The APIs do not currently expose a unified application/case, total counts, priority/due dates, or a shared cursor. The overview does not claim system-wide counts or urgent work.
- Raw document custody/review, legal/contract/financial onboarding, connection-method setup, and business approval stages need product decisions and API contracts before implementation.
- Existing customer activation and courier trust transitions remain the authoritative write paths with their current audit and authorization rules.

## Verification

- `npm test`: 11 tests passed.
- `npm run build`: TypeScript and Vite production build passed.
