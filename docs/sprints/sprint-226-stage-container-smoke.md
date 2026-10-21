# Sprint 226 — staged container runtime smoke

## Goal

Verify the exact Docker image intended for Stage starts against an isolated PostgreSQL test database and serves both the Operations Console and health endpoints.

## Delivered

- Extend the Stage smoke script to extract the built JavaScript bundle path from the served console and require that bundle to return HTTP 200.
- Test the missing-console and missing-JavaScript cases as failures in the smoke-script contract test.
- In Mono CI, build the API image, create a disposable database, run that image with Stage settings and migrations enabled, then smoke-test its console bundle and liveness/readiness endpoints.
- Preserve container logs on a failed smoke run and remove the test container on exit.

## Safety and scope

This is a CI-only test against an ephemeral PostgreSQL database and synthetic test state. It does not connect to Render, create infrastructure, deploy, alter secrets, or touch Production. The runtime smoke does not exercise authenticated operations; those checks still require approved Stage-only OIDC identities.

## Verification

- Exact-head Mono CI: smoke-script contract tests, isolated migrations, API health smoke, .NET tests, Docker build and container runtime smoke must pass.
- Exact-head Operations Console CI: type-check, build and UI tests must pass.
- The live Stage smoke remains part of the Stage step after the owner applies the Blueprint.
