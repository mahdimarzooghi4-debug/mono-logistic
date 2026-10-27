# Sprint 257: Merchant courier location view

Extends the existing Mono business app's delivery detail with the current courier location.

- A mission with an active status polls the customer-scoped location endpoint immediately and every 15 seconds while its detail is open.
- The view shows coordinates, reported accuracy, and the point timestamp, with a native map-app handoff link. It does not create a map from a public tile provider.
- The API returns only fresh locations; missing/stale points are presented as unavailable, and terminal missions stop polling.
- The Mono customer credential remains in memory for the existing session flow; no credential is added to the Henna frontend.

## Integration boundary

The Henna admin link/SSO handoff is a follow-up because neither a deployed Mono business-app URL nor a shared identity contract is available yet. The self-hosted OSM tile endpoint is also not provisioned, so this sprint does not claim an in-panel map view.
