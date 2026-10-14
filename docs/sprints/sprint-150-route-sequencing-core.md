# Sprint 150 — Route Insertion Sequencing Core

## Goal

Start the routing and batching epic with a deterministic domain module that evaluates where one Mission's pickup and drop-off can be inserted into an ordered route without breaking existing committed delivery times.

## Delivered

- Added `RouteInsertionTimeWindowPlanner`, a pure domain module that enumerates pickup/drop-off insertion positions while preserving the existing stop order.
- Evaluates directed travel-time legs, pickup readiness and dwell, drop-off service time, and every existing and candidate Mission deadline.
- Enforces the Phase 1 maximum of five Missions per Route.
- Returns `Unresolved` when the travel-time matrix is incomplete or its caller-provided source-verification flag is false.
- Added tests for insertion/deadline evaluation, missed candidate promise, the three-minute pickup dwell boundary, unresolved routing evidence, the Mission limit, and malformed stops.

## Scope boundary

This is the sequencing and time-window kernel. It does not acquire or verify map-provider data, calculate capacity or zone authorization, persist Routes or versions, expose an API, update Mission state, or authorize dispatch. Callers must not treat a candidate position as an overall feasibility pass.

## Remaining E11 work

- Route lifecycle, durable stop/version persistence, and concurrency control.
- Approved routing provider integration, source/freshness verification, and route simulation evidence.
- Capacity, zone, and full feasibility-gate integration.
- Mission/Promise orchestration and operations API integration.
- End-to-end behavior proving every insertion reruns simulation and cannot break an existing committed promise.

## Verification

Added focused xUnit coverage. Run `dotnet test tests/Mono.Api.Tests/Mono.Api.Tests.csproj` in a .NET 10 SDK environment. The current editing environment does not include the .NET SDK, so local test execution is unavailable.
