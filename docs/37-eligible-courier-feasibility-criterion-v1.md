# Eligible Courier Feasibility Criterion v1

## Purpose

Translate a supplied courier candidate assessment set into the E7.1 `Eligible couriers` criterion.

## Candidate set contract

The producer must mark the candidate set `COMPLETE`, `INCOMPLETE`, or `UNAVAILABLE`. Candidate results are per-courier and are `ELIGIBLE`, `EXCLUDED`, or `UNRESOLVED`.

- Any explicitly eligible courier proves the criterion `PASS`, even if the search was incomplete.
- `FAIL` is permitted only when the candidate set is complete and every candidate is definitively excluded, including a complete empty set.
- An incomplete or unavailable set, or any unresolved candidate when no eligible courier exists, returns `UNRESOLVED`.
- Candidate identifiers must be unique; failed and unresolved evidence must carry canonical reason codes.

## Boundary

This evaluator consumes evidence; it does not discover couriers or establish that the evidence producer is trustworthy. The current repository does not yet provide courier activation, live availability, or an operational candidate generator. No courier identity data is persisted or exposed by this contract, and this work does not add request intake, Promise, Mission, endpoint, or external integration.
