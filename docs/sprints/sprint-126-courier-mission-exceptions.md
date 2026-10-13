# Sprint 126 — Courier mission exceptions

## Goal

Give couriers a safe way to report execution blockers and give operations a traceable path to resolve them.

## Delivered

- Add structured courier issue codes: access blocked, recipient unavailable, package mismatch, safety concern, and other.
- Permit only the courier with an accepted Mission offer to create a report while the Mission is active.
- Store issue events append-only and expose a limited operations queue scoped to a Mission.
- Add idempotent operations resolution; resolving an issue changes only the issue report state, not the Mission lifecycle.
- Audit successful operations inbox reads in the Mission read-audit ledger.
- Add PostgreSQL integration coverage for wrong-courier denial, report replay/conflict, queue visibility, resolution, and resolution replay.

## Boundary

The report carries a fixed issue code and no free-text customer or recipient details. Issue reporting does not automatically fail, cancel, pause, or reassign the Mission. No GPS, maps/OSM, Stage, or deployment behavior is included.
