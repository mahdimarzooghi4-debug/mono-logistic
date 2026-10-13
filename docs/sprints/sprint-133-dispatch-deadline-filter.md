# Sprint 133 — Dispatch deadline filter

## Goal

Let operations isolate active offers by whether the response deadline has passed.

## Delivered

- Add the optional `deadline_passed` queue filter and bind it through cursor validation and page reads using one API-clock timestamp.
- `true` and `false` filter only Missions that currently have an active offer; omit the parameter to keep the full queue.
- Test both filter values before and after the controlled offer deadline.
- Update the dispatch operations API contract.
