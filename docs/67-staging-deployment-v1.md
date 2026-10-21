# Staging deployment v1

## Scope

The Render Blueprint provisions a staging-only API web service, Operations Console static assets in the same container/origin, and a PostgreSQL database. It does not target Production and does not include Production secrets. Automatic deployment is disabled so a staging build is deployed deliberately. The runtime supports health, console-shell, and database migration checks; optional authenticated and routing tests need the external prerequisites below.

## Database migration behavior

Mono.DbMigrator applies the ordered SQL files in db/migrations. Each file and its SHA-256 checksum are recorded in mono_schema_migrations in the same transaction as its DDL. A transaction-scoped advisory lock serializes deployments. Re-running skips matching applied migrations; a checksum mismatch stops startup rather than silently accepting edited history.

The container runs the migrator only when MONO_AUTO_MIGRATE=true. The staging Blueprint enables this setting. Other environments keep migration execution disabled unless their deployment owner explicitly opts in.

## Health checks

- /health/live reports process liveness without requiring PostgreSQL.
- /health/ready checks PostgreSQL connectivity and reports unhealthy when the database cannot be reached.
- `/` serves the compiled Operations Console from the same origin as `/operations/*`, avoiding browser CORS configuration. API authorization remains enforced by the API endpoints.

## Owner-supplied Stage prerequisites

Required before deploying:

- Confirm the Render workspace and ensure it can access this GitHub repository and create a web service plus PostgreSQL database. The Blueprint uses the free plan and Render's default region; if those are unavailable, agree on an alternative plan/region before changing the Blueprint.
- Keep all test records synthetic and Stage-only. Do not copy Production credentials or personal data into Stage.

Required only for authenticated API tests:

- Provide HTTPS OIDC Authority and Audience values for the Operations and/or Courier identity issuer, configured in Render as `MonoOperations__Authority`, `MonoOperations__Audience`, `MonoCourier__Authority`, and `MonoCourier__Audience` as applicable.
- Provide Stage-only test identities. An Operations administrator token needs one `sub`, `mono/role=MONO_ADMIN`, and issuer-verified `mono/mfa=true`. A Courier token needs `sub` and `mono/role=COURIER`, with that issuer/subject already bound to an active, unsuspended synthetic Courier in the Stage database.
- Enter tokens and secret values only through the identity provider or Render's secret configuration; do not put them in GitHub or chat.

Required only for specific integration tests:

- Payout-account encryption tests need a Stage-only 32-byte Base64 key at `MonoCourier__PayoutIbanEncryptionKey`. Webhook-secret tests need a separate Stage-only 32-byte Base64 key at `Mono__WebhookEncryptionKey`.
- Live route-matrix tests need a chosen Maps provider and its Stage API credential. The current runtime deliberately uses `UnconfiguredMapsProvider`, so real route estimates are not available until a provider is selected and its adapter is implemented.
- Webhook delivery tests need a Stage-only public HTTPS receiver on port 443. The API rejects local and non-public webhook hosts.

## Stage test checklist

1. Create the Blueprint in the confirmed Render workspace and review the web-service and PostgreSQL resources before applying it.
2. Confirm the service branch matches the `branch` value in `render.yaml` (`sprint-225-stage-console-serving`) and that no Production database or secrets are referenced.
3. Apply the Blueprint, then wait for the service deploy to become live.
4. Run `MONO_STAGE_BASE_URL=https://<stage-host> tools/stage-smoke.sh` and confirm `/`, `/health/live`, and `/health/ready` return HTTP 200; the root response must contain the Operations Console mount and the referenced JavaScript asset must return HTTP 200.
5. Review deploy logs for the ordered migration list and Database migrations complete.
6. Run only the authenticated or integration smoke tests whose Stage prerequisites are configured, using Stage-only identities and synthetic data.
7. Preserve required test evidence before tearing down the staging database.
