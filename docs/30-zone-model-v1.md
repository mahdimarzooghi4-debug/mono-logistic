# Zone Model v1

## Purpose

Represent Mono's geographic hierarchy as City → Operating Zone → Micro-zone while keeping geometry and capacity behavior outside the model.

## Hierarchy

- A City is a root and has no parent.
- An Operating Zone must reference a City parent.
- A Micro-zone must reference an Operating Zone parent.
- Zone IDs are opaque, required, and unique within the validated registry.
- Names are required.

The City node is the canonical city context for its descendant operating and micro-zones.

## Metadata boundaries

- geometry_reference is optional and opaque; no geometry format, coordinate system, boundary algorithm, or provider is selected.
- status is optional opaque metadata; this contract defines no status values or transitions.
- capacity_policy_id is optional opaque metadata; this contract does not calculate or allocate capacity.
- An empty registry is valid and means no zone definitions have been supplied.

## Out of scope

This model does not determine whether a coordinate belongs to a zone, activate or retire zones, authorize couriers, decide coverage, calculate capacity, persist definitions, or expose operations APIs. A Zone is a hard boundary in the approved business architecture, but it cannot be enforced until geometry and lifecycle behavior are defined.
