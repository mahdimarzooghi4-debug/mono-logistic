# Courier Zone Approval v1

## Purpose

Represent a courier's preferred operating zones and the subset Mono has approved, building on the City → Operating Zone → Micro-zone registry in [Zone Model v1](30-zone-model-v1.md).

## Contract

- `courier_id` is required.
- `preferred_zone_ids` records the courier's requested zone IDs.
- `approved_zone_ids` records the Mono-approved subset of those preferences.
- All referenced IDs must exist in the supplied valid zone registry; identifiers are compared ordinally after trimming.
- Duplicate and blank zone IDs are invalid.
- `primary_zone_id` is optional and, when present, must identify an approved zone.
- Empty preference and approval lists are valid and do not imply operational eligibility.

The model does not assume that zones must be micro-zones, interpret zone status, or infer approval from preference. Mono must make the approval decision explicitly.

## Boundary and lifecycle limits

The approved zone list expresses the intended hard operating boundary. This structural validator does not determine whether a GPS point or Mission location lies within a zone: the current zone model leaves geometry opaque. It also does not grant runtime assignment permission.

Approval workflow, audit history, zone activation, geometry membership, persistence, authorization, online availability, capacity, and operations APIs remain outside this contract.
