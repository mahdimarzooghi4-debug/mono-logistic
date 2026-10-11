# Sprint 088 — Delivery Request lifecycle timeline

## Goal

Let Hana inspect the recorded Promise and Mission milestones associated with a Delivery Request, including events that occur before Mission confirmation.

## Changes

- Add `GET /v1/delivery-requests/{id}/timeline` under `delivery-requests.read`.
- Resolve timeline events through the authenticated tenant's Promise and Mission aggregates.
- Expose only `promise.committed`, `promise.revised`, and `mission.created` event envelopes, ordered by occurrence time.
- Omit event payloads and return an empty timeline for an existing request without recorded lifecycle events.
- Add PostgreSQL-backed HTTP tests for event ordering, tenant isolation, unknown requests, and read-scope access.
- Update the API contract and product backlog.

## Security and scope

Timeline reads verify the Delivery Request belongs to the authenticated customer before returning any events. They include only explicitly allowlisted event types and never serialize the outbox payload. Quote state remains on the detail endpoint. No lifecycle mutation, Stage work, or deployment is included.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and API container image build on this Draft PR.
