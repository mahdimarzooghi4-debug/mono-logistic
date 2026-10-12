# Zone geometry activation (Sprint 095)

Mono administrators may explicitly activate or deactivate a persisted geometry snapshot through the operations API.

- POST /operations/v1/zones/{zoneId}/geometry/{geometryVersion}/activate
- POST /operations/v1/zones/{zoneId}/geometry/{geometryVersion}/deactivate
- GET /operations/v1/zones/{zoneId}/geometry-activation

Activation requires the exact stored snapshot SHA-256 and the latest review decision for that snapshot to be APPROVE. Switching versions atomically appends a deactivation event for the prior version and an activation event for the new one. Per-zone transaction locks serialize competing changes. The database independently checks the snapshot hash and review condition and prevents mutation of event history.

The active pointer is an administrative record only. Delivery eligibility, route planning, public APIs, customer assignments, and live OSM imports do not consume it in this sprint.
