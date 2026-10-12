# Sprint 001 — Platform Foundation

- Status: In progress
- Phase: A — Platform Foundation
- Epic: A1 — Repository & Engineering Foundation
- Branch: `sprint-001-platform-foundation`
- Base: `docs/business-foundation`

## Sprint goal

Create the smallest runnable and testable Mono application foundation on the accepted .NET stack, without introducing incomplete logistics behavior.

## Scope

- Add a .NET 10 solution and ASP.NET Core API project.
- Add liveness and readiness endpoints.
- Add integration tests for both health endpoints.
- Add a multi-stage Docker image build and verify it in GitHub Actions CI.
- Document that health endpoints currently verify process health only; database, queue, and map checks are not yet connected.

## Acceptance criteria

1. `dotnet restore Mono.sln` succeeds.
2. `dotnet build Mono.sln --configuration Release` succeeds with no compile errors.
3. `dotnet test Mono.sln --configuration Release` passes both endpoint tests.
4. `docker build --tag mono-api:ci .` succeeds.
5. Both `/health/live` and `/health/ready` return HTTP 200 and report `Healthy`.
6. No customer, courier, mission, location, or financial production data is fabricated.
7. No secrets or external AI/model API integrations are introduced.

## Out of scope

- Authentication, tenant access, database migrations, mission lifecycle, pricing, maps, routing, courier assignment, mobile clients, and AI inference.
- Staging, QA gate, release approval, and production deployment.

## Verification record

- Local verification: unavailable in the current workspace because the .NET SDK is not installed and network access prevents cloning the repository.
- Required verification: GitHub Actions CI on this branch and review of the exact tested commit.
