# Sprint 229 — Verify Non-root Container Runtime

## Goal

Turn the API image's non-root runtime setting into a staging release check.

## Delivered

- Inspect the built `mono-api:ci` image before starting it.
- Require the configured runtime UID to be numeric and non-zero.
- Keep the existing disposable PostgreSQL container smoke after the check.
- Document and trace the check in the Stage runbook and product backlog.

## Verification

Mono CI fails before container startup if the image is configured with root, an empty user, or a non-numeric identity. The rest of CI still validates migrations, health endpoints, console assets, and live container behavior.

## Boundary

CI and documentation only. No Docker base image, runtime configuration, Render resource, secret, or Production behavior is changed.
