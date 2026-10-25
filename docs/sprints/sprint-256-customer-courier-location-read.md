# Sprint 256: Customer live courier location

Adds a tenant-scoped read endpoint for a courier's current point on an active Mission.

- `GET /v1/missions/{missionId}/courier-location` requires the existing `delivery-requests.read` customer API scope.
- The query binds the Mission to the authenticated customer, requires an accepted courier offer, and only allows non-terminal delivery lifecycle states.
- Points older than 60 seconds or with a future timestamp are unavailable. The endpoint returns only latitude, longitude, accuracy, and observation time; it does not expose courier identity or route history.
- A missing, stale, unassigned, completed, or foreign-tenant Mission returns `404 COURIER_LOCATION_UNAVAILABLE`.

The integration test covers tenant isolation, terminal missions, and a fresh assigned point. External customer authentication and map UI integration remain deployment/product integration work.
