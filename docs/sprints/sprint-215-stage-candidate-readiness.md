# Sprint 215 — Stage candidate readiness

## Goal
Make the cumulative backend candidate deployable to a separate Stage environment and make owner-supplied test prerequisites explicit.

## Delivered
- Pin the Render Blueprint to `sprint-215-stage-candidate` and keep automatic deploys disabled.
- Record required and conditional OIDC, encryption, maps, webhook receiver, workspace, billing, and synthetic-data prerequisites.
- Keep Production resources, secrets, and data outside the Stage setup.

## Verification
- Official Render JSON Schema validation passes for `render.yaml`.
- GitHub Actions build, migration validation, API health smoke test, test suite, and container build run on the PR head.
- The reusable `tools/stage-smoke.sh` checks both health endpoints; CI uses it against the local API and the Stage checklist uses it against the deployed service.
- Mocked contract tests cover remote HTTPS enforcement, localhost HTTP, non-200 status, and transport failure.
- No Render resources have been created or changed.

## External boundary
- Render platform validation and resource creation require confirmation of the target workspace.
- Live map-matrix testing requires a selected provider and an implemented provider adapter.
- Authenticated tests require Stage-only OIDC identities and synthetic records.
