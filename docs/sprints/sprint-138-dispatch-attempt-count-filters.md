# Sprint 138 — Dispatch attempt count filters

## Goal

Let operations isolate new dispatch work from Missions that have already received one or more courier offers.

## Delivered

- Add optional inclusive `min_attempt_count` and `max_attempt_count` filters to the administrator-only dispatch queue.
- Count all offers created for the Mission, including completed historical attempts.
- Reject negative bounds and ranges where the minimum is greater than the maximum.
- Apply the same filters to initial reads, subsequent pages, and cursor validation.
- Add PostgreSQL integration coverage for both bounds and invalid ranges.
- Update the dispatch API contract, backlog trace, and README.

## Boundary

The filters are read-only. They do not affect offer creation, courier selection, queue ordering, or Mission state.
