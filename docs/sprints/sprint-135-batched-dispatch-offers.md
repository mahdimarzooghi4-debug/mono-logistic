# Sprint 135 — Batched dispatch offers

## Goal

Reduce repetitive operations requests when explicitly offering separate searching Missions to selected couriers, while preserving each offer's consent, authorization, and independent transaction.

## Delivered

- Add an administrator-only batch offer endpoint for 1–20 Mission/courier/zone commands.
- Validate the complete command list before executing any offer and reject duplicate Mission IDs and invalid identifiers or idempotency keys.
- Reuse the existing single-offer lifecycle for every item; report each outcome independently and preserve safe retry through per-Mission idempotency.
- Add PostgreSQL integration coverage for duplicate-Mission preflight, no side effects on rejection, successful offer creation, and replay.
- Document partial outcomes and retry behavior.

## Boundaries

- The batch endpoint does not choose couriers, rank candidates, create offers automatically, or accept an offer on the courier's behalf.
- Each command commits independently; partial success is possible.
- No Stage or deployment.
