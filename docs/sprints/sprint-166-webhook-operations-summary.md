# Sprint 166 — Webhook operations summary

## Objective

Provide a read-only backend feed for an internal operations dashboard to monitor webhook delivery queue health.

## Scope

- Add an operations-only aggregate endpoint for queue status counts, due work, expired leases, recent dead letters, and oldest open work.
- Require the existing Mono administrator policy, which enforces verified MFA.
- Record every privileged summary read in an append-only audit table with the authenticated operations actor.
- Return aggregate counts only; expose no customer payload, callback URL, or secret.

## Acceptance

- Non-administrators and operations tokens without verified MFA cannot access the summary.
- Counts reflect persisted queue state and use the server's current time.
- Each successful read creates one immutable audit record and records the number of deliveries summarized.
- CI build, migrations, health checks, tests, and image build pass.
