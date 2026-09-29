# Mono Business Architecture

## 1. Company Definition

Mono is a **logistics technology and operations platform**.

Mono does not define itself as a courier company or a carrier. Its role is to:

1. receive logistics demand from a business,
2. evaluate feasibility,
3. choose the best execution plan,
4. orchestrate couriers/providers,
5. control delivery promises and SLA,
6. track execution and exceptions,
7. settle financially with the execution side.

## 2. Long-term Vision

Mono plans to enter multiple logistics service lines over time:

- Urban logistics
- Intercity logistics
- Ground freight
- Air freight
- Sea freight
- Later: warehousing, fulfillment, reverse logistics, and complementary services

Urban delivery is only the **first service line**, not the final identity of Mono.

## 3. Shared Core

All future service lines should be built on a shared orchestration core:

```
Request
  -> Feasibility
  -> Decision
  -> Mode / Provider / Courier Selection
  -> Promise
  -> Execution
  -> Tracking
  -> Exception Management
  -> SLA Control
  -> Settlement
```

Each logistics vertical may have its own rules and providers, but should reuse the common Mono core.

## 4. Phase 1

Phase 1 is limited to **urban logistics**.

The first customer is **Hana**.

Hana outsources its complete urban logistics operation to Mono.

Mono's phase-1 solution is intended to become a repeatable B2B logistics solution that can later be sold to platforms similar to Hana.

## 5. Mono's Role in Phase 1

Mono owns:

- feasibility decision,
- delivery promise,
- courier-mode selection,
- route construction,
- batching,
- courier allocation,
- SLA control,
- exception handling,
- tracking,
- courier earnings calculation,
- courier balance and withdrawal logic.

Mono does **not need to own a fleet**.

## 6. Core Principle

> Mono owns the delivery promise.

Hana submits the logistics request. Mono decides whether it can fulfill it and, if accepted, commits the delivery time and fare.
