# Sprint 137 — Dispatch delivery deadline filter

## Goal

Help operations prioritize searching Missions whose committed delivery promise is approaching by narrowing the dispatch queue to an inclusive delivery-time cutoff.

## Delivered

- Add optional `delivery_before` to the administrator-only dispatch queue API.
- Filter by `missions.committed_delivery_at <= delivery_before`, using an explicit UTC ISO 8601 timestamp.
- Apply the same cutoff when validating pagination cursors, so a cursor from a different filter set is rejected.
- Add PostgreSQL integration coverage for the inclusive boundary and for a cutoff immediately before the committed delivery time.
- Document the API contract, backlog trace, and Sprint 137.

## Boundary

This is a read-only prioritization filter. It does not reorder the queue, change a Mission or delivery promise, or trigger dispatch actions. Stage and deployment remain outside this sprint.
