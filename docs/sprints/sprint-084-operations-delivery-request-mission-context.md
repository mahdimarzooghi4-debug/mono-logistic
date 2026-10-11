# Sprint 084 — Operations Delivery Request Mission context

## Goal

Give Mono operations one safe view of each Delivery Request and its confirmation state, so Hana orders can be identified before and after they become Missions.

## Changes

- Join Delivery Requests to their tenant-scoped Mission, when present.
- Include Mission ID, Mission status, and committed delivery time in list and detail summaries.
- Add customer ID, branch ID, and Mission-existence filters to the paginated search.
- Bind cursor validation to every active filter.
- Keep stored request payloads, locations, and contact information out of operations responses.
- Add PostgreSQL integration coverage and API/backlog documentation.

## Security and scope

All routes continue to require the Mono administrator policy with verified MFA. Search and detail reads continue to write to the append-only access audit. This package is read-only; it does not confirm requests, change Mission state, touch Stage, or deploy.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and API container image build on this Draft PR.
