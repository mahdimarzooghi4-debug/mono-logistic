# Sprint 136 — Dispatch outcome filter

## Goal

Let operations focus the dispatch queue on Missions whose most recent offer reached a selected lifecycle outcome.

## Delivered

- Add the optional `latest_action` filter to the operations dispatch queue.
- Support exact, case-sensitive offer actions: OFFERED, ACCEPTED, DECLINED, WITHDRAWN, EXPIRED, RELEASED, CANCELLED, and COMPLETED.
- Apply the filter consistently to first-page reads and cursor validation; reject unsupported values and cursors that do not match the active filters.
- Add PostgreSQL-backed coverage for offered, expired, empty outcome, and invalid-filter cases.
- Update the dispatch API contract and backlog.

## Boundary

The filter is read-only. It does not select a courier, change Mission state, or automatically re-offer a Mission.
