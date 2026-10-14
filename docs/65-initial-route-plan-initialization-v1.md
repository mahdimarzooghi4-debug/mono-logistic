# Initial Route Plan Initialization v1

## Goal

Create the first immutable Route plan only after a trusted travel-time source verifies the exact route origin, confirmed Mission inputs, Promise deadline, coordinates, mode, and directed matrix.

## Contract

- The initializer loads the Mission from PostgreSQL only when it is in `PROMISED` state. The Mission's foreign key binds it to its immutable Promise version and committed deadline; the evidence digest includes the Promise ID/version and location IDs derived from its persisted DeliveryRequest ID. When no `estimated_ready_at` was recorded, Mission creation time is the conservative pickup-ready fallback.
- The route begins from an explicit stable `StartLocationId` at an explicit UTC `StartedAt`. Pickup/drop-off service durations are explicit inputs because no service-time policy is selected in the repository yet.
- The evaluator checks directed travel from start to pickup and pickup to drop-off, readiness waiting, service time, the existing three-minute pickup dwell ceiling, and the Mission's committed delivery deadline.
- Missing matrix cells or unverified evidence are unresolved. A deadline or dwell violation is infeasible. Neither outcome writes a Route version.
- Successful evaluation creates exactly one Mission deadline and its ordered pickup/drop-off stops. Persistence uses the existing optimistic/idempotent route version store and writes version 1.
- Matrix acquisition is provider-neutral and unsigned; the configured source verifier must attest to the complete canonical evidence payload before persistence.

## Scope boundary

This initializes route sequencing state only. It does not create or change Mission or Promise state, authorize courier capacity or zones, select an initial start location, dispatch a courier, or claim overall feasibility. The caller must supply an approved route origin, start time, travel mode, and service durations.
