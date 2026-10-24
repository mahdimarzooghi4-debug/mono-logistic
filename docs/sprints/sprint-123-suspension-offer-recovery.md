# Sprint 123 — Suspension offer recovery

## Goal

Prevent unanswered Mission offers from remaining active after their courier is operationally suspended.

## Delivered

- Add a database trigger on the append-only courier operational-status event stream.
- On `SUSPENDED`, withdraw only that courier's `OFFERED` missions using system-authored `WITHDRAWN` events.
- Keep each Mission searching for another courier and free its active-offer slot.
- Leave accepted offers unchanged because they represent an assignment that needs a separate reassignment workflow.
- Add integration coverage for suspension, automatic withdrawal, reinstatement, and a new offer.

## Boundary

Suspension recovery affects pending offers only. It does not automatically reassign an accepted Mission, mark the courier offline, or change maps/GPS, Stage, or deployment.
