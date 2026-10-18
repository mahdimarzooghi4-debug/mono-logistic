# Sprint 214 — Worker batch counters

## Goal
Let Operations see whether webhook delivery and Mission offer-expiry workers are recovering or repeatedly failing between status polls.

## Delivered
- Add cumulative total, successful, and failed batch counters to both worker status snapshots.
- Increment counters atomically with the existing status transition.
- Keep metrics process-local and exclude blocked/disabled startup states from batch totals.
- Verify counters across failure and recovery and verify zero counters for disabled/blocked workers.

## Verification
- GitHub Actions runs build, migration validation, API smoke tests, tests, and container validation for this draft.
