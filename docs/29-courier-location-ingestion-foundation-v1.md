# Courier Location Ingestion Foundation v1

## Purpose

Define a validated internal GPS update and a deterministic per-courier current-position projection primitive.

## GPS update fields

- Courier ID
- WGS84 latitude and longitude
- optional accuracy in metres
- optional heading in degrees
- optional speed in metres per second
- observed timestamp
- optional active route ID

The contract is internal groundwork. It does not authenticate the courier or prove that a route is active.

## Validation

- Courier ID and observed timestamp are required.
- Coordinates must be finite WGS84 values.
- Optional accuracy and speed values must be finite and non-negative.
- Optional heading must be finite and in the interval [0, 360).
- Timestamps normalize to UTC.
- Unknown GPS event age limits are not guessed.

## Projection and freshness

For a single courier, a position replaces the current projection only when its observed timestamp is strictly newer. Older or equal-time updates do not replace it. A caller may evaluate freshness by supplying both the current time and an explicit maximum age; there is no default freshness threshold.

The projection primitive is deterministic and in-memory only. Persistence, concurrency control, history, and API exposure are not part of this sprint.

## Privacy and activation boundary

Security architecture requires location collection to be purpose-limited and mission-aware. The repository does not yet have authenticated Courier identity, Mission or Route lifecycle, a tracking authorization policy, or retention rules. Therefore this sprint does not expose a GPS endpoint or accept live location streams. Raw GPS updates must not be published as customer-facing domain events by default.
