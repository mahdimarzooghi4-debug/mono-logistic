# Sprint 208 — Mission delivery SLA alert feed

## Goal
Make imminent and missed committed-delivery deadlines visible across the full active Mission lifecycle, including Missions already assigned to a courier.

## Delivered
- Add administrator-only `GET /operations/v1/sla/delivery-alerts`.
- Include all active Mission lifecycle states and show the assigned courier when one exists.
- Default to overdue/due-now Missions; allow Operations to choose a bounded lookahead window up to 10,080 minutes.
- Evaluate each page against a single captured UTC time and preserve that time across keyset pages.
- Bind cursors to the customer scope and lookahead window.
- Record every page, including empty results, in the append-only Mission operations read audit.
- Add migration, registration, sprint backlog traceability, and audit integration coverage.

## Security and behavior
- Only structured Mission identifiers, lifecycle state, customer scope, committed delivery time, and accepted courier identifier are returned.
- Terminal Missions are excluded; the alert feed does not change Mission state or dispatch decisions.
- The lookahead is selected by the operator; this change does not define or infer a new business SLA threshold.
