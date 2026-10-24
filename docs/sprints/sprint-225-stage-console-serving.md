# Sprint 225 — staging console serving preflight

## Goal

Make the reviewed Operations Console available from the existing staging API service without a separate browser origin or a permissive CORS policy.

## Delivered

- Build the Vite Operations Console in a Node build stage in the API Dockerfile and copy only its production assets into the ASP.NET API's `wwwroot`.
- Serve the console entry point and static assets from the API origin; API routes and authorization remain unchanged.
- Retarget the staging Blueprint from the obsolete `sprint-215-stage-candidate` branch to this reviewed candidate branch, while keeping `autoDeploy: false`.
- Extend `tools/stage-smoke.sh` to require the console mount and built asset reference as well as both liveness and database readiness checks.
- Update the Stage runbook with the same-origin serving model and candidate branch.

## Safety and scope

This package changes staging build/serving configuration only. It does not deploy, create or modify Render resources, change secrets, run migrations, issue credentials, or touch Production. Stage uses its separate PostgreSQL database and must use synthetic records. Authenticated API checks still need an owner-configured HTTPS OIDC issuer and Stage-only administrator identity with verified MFA.

## Verification

- Exact-head GitHub CI and Operations Console workflows must pass.
- Docker image build must include the console assets and API.
- After the owner applies the Stage blueprint, `tools/stage-smoke.sh` checks the root console shell, built asset reference, `/health/live`, and `/health/ready`.
