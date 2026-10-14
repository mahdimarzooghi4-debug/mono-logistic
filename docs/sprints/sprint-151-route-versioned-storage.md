# Sprint 151 — Versioned Route Plan Storage

## Goal

Add the durable route-plan foundation needed to build and revise Routes while keeping each accepted planning snapshot reviewable and preventing concurrent writers from silently replacing a newer plan.

## Delivered

- Added a PostgreSQL-backed repository for immutable Route plan versions, ordered stops, and the committed delivery deadline associated with each Mission.
- Added expected-version writes, command idempotency, and per-Route concurrency locking. A stale writer receives `Conflict`; an exact retry receives `Replayed`.
- Require a new Route to start with one Mission. Each subsequent version inserts exactly one Mission pickup/drop-off pair while preserving all existing stops and committed delivery deadlines.
- Hash each snapshot and verify the hash and shape when reading it back.
- Add database guards for append-only snapshots and sequential advancement of the current-version pointer.
- Register the repository for internal application use and add domain plus PostgreSQL integration tests.

## Scope boundary

This stores planning snapshots; it does not run the route-time-window planner, call a Maps provider, establish source provenance, check capacity or Zones, authorize writers through a public endpoint, advance Mission state, or authorize dispatch. Stored versions must not be treated as proof of feasibility.

## Remaining E11 work

- Bind route insertion persistence to a complete simulation and independently verified routing evidence.
- Add capacity and Zone checks to the route feasibility path.
- Define the approved Route lifecycle and integrate the route plan with Mission/Promise orchestration.
- Expose authorization-controlled APIs and operations needed by actual route owners.
- Test the full insert → simulate → preserve Promise → persist transaction as one operational flow.

## Verification

Run `dotnet test tests/Mono.Api.Tests/Mono.Api.Tests.csproj` with the .NET 10 SDK and PostgreSQL configured through `MONO_TEST_DATABASE`.
