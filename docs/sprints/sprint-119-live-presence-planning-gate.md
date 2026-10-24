# Sprint 119 — Live presence in courier planning evidence

## Goal

Expose current live courier presence beside the existing planned-capacity calculation so operations can distinguish couriers who are scheduled from couriers who have actually reported being online.

## Change

The existing paginated operations planning eligibility read now returns separate evidence:

- `planned_capacity_contributor` continues to mean ACTIVE, unsuspended, approved for the requested zone, and covered by an overlapping planned window.
- `is_online`, `live_presence_zone_id`, and `live_presence_updated_at` describe the latest self-reported operational presence.
- `live_presence_candidate` is true only for an ACTIVE, unsuspended courier whose approved zone matches the requested zone and whose current live presence is ONLINE in that same zone.
- `live_presence_exclusion_reasons` explains why the courier does not qualify for that live-presence signal.
- Planned-capacity exclusion reasons remain independent of live-presence reasons.

The response classification is `PLANNED_CAPACITY_AND_LIVE_PRESENCE_SIGNALS`. It is evidence for an operations user, not authorization to assign or dispatch a mission. Planned windows do not make an offline courier live, and being online does not create a planned capacity window.

## Boundary

This remains a read-only operations preview. It does not assign couriers, claim/lock capacity, accept missions, track coordinates, or collect GPS. Map and OSM integration remain deferred.

## Validation

The PostgreSQL integration test covers an online courier with a scheduled window, an offline courier, a suspended courier, and a non-active courier. It verifies the independent planned and live-presence signals, their exclusion reasons, pagination, and read auditing.
