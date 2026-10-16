# Staging deployment v1

## Scope

The Render Blueprint provisions a staging-only web service and PostgreSQL database. It does not target Production and does not include Production secrets. Automatic deployment is disabled so a staging build is deployed deliberately.

## Database migration behavior

Mono.DbMigrator applies the ordered SQL files in db/migrations. Each file and its SHA-256 checksum are recorded in mono_schema_migrations in the same transaction as its DDL. A transaction-scoped advisory lock serializes deployments. Re-running skips matching applied migrations; a checksum mismatch stops startup rather than silently accepting edited history.

The container runs the migrator only when MONO_AUTO_MIGRATE=true. The staging Blueprint enables this setting. Other environments keep migration execution disabled unless their deployment owner explicitly opts in.

## Health checks

- /health/live reports process liveness without requiring PostgreSQL.
- /health/ready checks PostgreSQL connectivity and reports unhealthy when the database cannot be reached.

## Stage test checklist

1. Create the Blueprint in the intended Render workspace and review the web-service and PostgreSQL resources before applying it.
2. Confirm the service branch is the staging-readiness branch and that no Production database or secrets are referenced.
3. Apply the Blueprint, then wait for the service deploy to become live.
4. Confirm /health/live and /health/ready both return HTTP 200.
5. Review deploy logs for the ordered migration list and Database migrations complete.
6. Run the selected authenticated API smoke tests using staging-only credentials and test data.
7. Tear down the staging database only after preserving any test evidence that must be retained.
