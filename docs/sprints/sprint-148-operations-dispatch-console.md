# Sprint 148 — Mono operations dispatch console

## Goal

Provide a usable Persian RTL internal operations workspace backed by the approved Mono operations APIs.

## Scope

- Dispatch queue and Mission search/detail/timeline/audit views.
- Courier directory, profile and trust history, zone capacity and scheduled work windows.
- Quote-derived Mission fare review using the immutable quote snapshot, with explicitly estimated allocations.
- Same-origin API requests, in-memory bearer token handling, and a dedicated TypeScript build check.

## Boundary

The console does not create Missions, mutate courier trust, assign or dispatch couriers, expose location, or create payments or settlement records. Scheduled availability changes use the existing guarded APIs and do not grant dispatch authority. Production identity configuration, Stage, deployment, and release remain separate gates.

## Validation

The exact PR head passed the .NET build/tests, container build, TypeScript check, and Vite build.
