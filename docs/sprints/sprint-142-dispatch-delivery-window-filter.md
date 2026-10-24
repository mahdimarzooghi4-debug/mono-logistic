# Sprint 142 — Dispatch delivery window filter

## Goal

Let Mono operations narrow the dispatch queue to committed delivery promises within a time window.

## Delivered

- Add optional inclusive `delivery_after` to the dispatch queue API.
- Combine `delivery_after` with the existing inclusive `delivery_before` upper bound.
- Reject inverted windows where the lower bound is later than the upper bound.
- Apply both bounds in first-page, later-page, and cursor-validation queries.
- Cover exact boundaries, empty windows, inverted windows, and cursor consistency in PostgreSQL integration coverage.
- Update the dispatch API contract, backlog, and README.

## Boundary

This is a read-only queue filter. It does not change a Mission, delivery promise, or dispatch action.
