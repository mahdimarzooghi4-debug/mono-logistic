# Zone Capacity v1

## Purpose

Represent the three capacity figures Mono tracks for a zone: registered, online, and effective.

## Snapshot contract

Each snapshot identifies a zone and an observation timestamp, and carries separate non-negative integer counts:

- `registered_count`: couriers approved to operate in the zone.
- `online_count`: couriers currently online in the zone.
- `effective_count`: couriers currently able to accept a new Mission, including couriers whose existing Route is about to complete.

The zone ID must exist in the supplied valid Zone registry. The timestamp is an offset-aware `DateTimeOffset`; it allows consumers to identify when the snapshot was observed.

The contract does not calculate any count from courier records or infer an ordering between the three values. It defines no Hana staffing target, capacity threshold, zone health, availability decision, promise behavior, or capacity policy.

## Out of scope

Persistence, aggregation, freshness thresholds, Hana's registered-capacity target, capacity allocation, zone health, courier assignment, operations APIs, Stage, and deployment remain future work.
