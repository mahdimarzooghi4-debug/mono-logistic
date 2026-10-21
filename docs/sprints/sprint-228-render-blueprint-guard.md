# Sprint 228 — Stage Blueprint Safety Contract

## Goal

Make critical staging-only infrastructure assumptions executable in CI so unsafe Blueprint edits fail before any owner applies the Blueprint.

## Delivered

- Parse `render.yaml` with Ruby's standard YAML library.
- Require the API web service to use the Dockerfile, a Sprint candidate branch, disabled automatic deployment, and the database readiness health endpoint.
- Require the Stage environment, Stage-only auto-migration setting, and connection to the named Stage database.
- Require the Stage database to remain private from external IP access and reject Production-like resource names.
- Add negative tests for auto-deploy, Production environment/database reference, main-branch tracking, public database access, and an incorrect health endpoint.

## Verification

The contract tests run in Mono CI and use only local YAML fixtures and synthetic values. No Render API calls, resource changes, deployment, secret, or Production data is involved.

## Boundary

This is a local structural guard for the current Blueprint contract. It does not validate Render platform availability or replace the required owner review before Blueprint application.
