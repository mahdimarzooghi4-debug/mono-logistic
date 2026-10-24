# Sprint 205 — Courier presence operations search

## Goal
Give Operations administrators a bounded, auditable roster of each courier's current self-reported presence.

## Delivered
- Add an administrator-only search endpoint with status, zone, and updated-time filters.
- Return a stable courier-ID keyset page and bind cursors to every normalized filter.
- Audit each successful page, including empty results.
- Include couriers with no presence projection as OFFLINE with no update timestamp.
- Add the schema constraint update, query index, service registration, route mapping, and database integration coverage.

## Security and behavior
- Responses expose courier ID, ONLINE/OFFLINE status, current zone, and update time only.
- The endpoint stores or returns no coordinates or location history.
- Live presence remains self-reported and is not by itself a dispatch-eligibility decision.
