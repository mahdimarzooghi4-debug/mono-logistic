# Maps, GPS, Location & Routing Architecture

## 1. Principle

GPS does **not** provide a map.

GPS provides the courier's geographic position as coordinates such as:

- latitude
- longitude
- accuracy
- heading
- speed
- timestamp

Mono must use a dedicated map/location layer for map rendering, geocoding, routing, ETA calculation, zone validation, and live tracking.

## 2. Core Architecture

```
Courier GPS
  -> Mono Location Service
  -> Mono Maps Gateway
  -> Map / Routing Provider
  -> Mono Routing & Promise Engine
```

Mono must not hard-code core business logic directly against a single map provider.

## 3. Mono Maps Gateway

Mono should expose an internal abstraction layer called:

> **Mono Maps Gateway**

Core services should call Mono's internal interface, not a provider-specific API.

Example capabilities:

```
geocode(address)
reverseGeocode(lat, lng)
getRoute(origin, destination, mode)
getETA(origin, destination, mode)
getRouteMatrix(points, mode)
```

The Maps Gateway may use one or more providers behind the scenes. Self-hosted map tiles are a client rendering source; they do not replace the routing provider or require tile requests to pass through the route API.

This allows Mono to change providers based on price, coverage, quality, reliability, regional availability, routing accuracy, and business constraints.

## 4. Required Map & Location Capabilities

Phase-1 urban logistics requires at least:

### Map Rendering

Display maps to couriers, operators, and where needed customers. The selected direction for the initial Iran deployment is to build OSM-derived vector tiles with Planetiler, serve a versioned MBTiles archive through Mono's owner-operated tile service, and render it with MapLibre. See [Self-Hosted OpenStreetMap Map Tiles Decision v1](69-self-hosted-osm-map-tiles-decision-v1.md).

### Geocoding

Convert address information into coordinates. A provider has not been selected.

### Reverse Geocoding

Convert coordinates into a human-readable address. A provider has not been selected.

### Routing

Calculate movement paths between pickup and dropoff points. The separate routing decision selects a self-hosted Valhalla candidate behind Mono Maps Gateway for server-side estimates.

### ETA

Estimate travel and arrival time. These are estimates and do not include live traffic unless a future traffic source is selected.

### Route Matrix / Multi-stop Routing

Evaluate a sequence of stops for batching and route simulation.

### Live Courier Tracking

Receive courier location updates during active operations.

### Zone Validation

Verify whether courier and Mission activity is inside approved operating zones.

## 5. Mode-aware Routing

Routing must be aware of delivery mode.

Supported Phase-1 modes:

- Walk
- Bike (bicycle)
- Motorcycle
- Car

Bike means bicycle; Motorcycle is a separate motorized profile. The same origin and destination may produce different routes and ETAs for Walk, Bike, Motorcycle, and Car.

Mono's Promise Engine and Batching Engine must therefore request routing using the actual candidate mode.

## 6. Location Data Model

Suggested courier location event:

```
courier_id
lat
lng
accuracy
heading
speed
timestamp
active_route_id
```

Additional fields may be added as required by the mobile platform or routing implementation.

## 7. Mission-aware Tracking

Mono should not continuously track a courier without an operational reason.

Suggested behavior:

### Offline

No operational location streaming.

### Available

Low-frequency location updates sufficient for supply and assignment decisions.

### Active Mission / Route

Higher-frequency location updates suitable for live tracking, ETA recalculation, SLA monitoring, route progress, and exception detection.

### Route Completed

High-frequency Mission tracking stops.

Tracking policy should remain privacy-conscious and tied to legitimate operational use.

## 8. Consumers of Location Data

```
Location Stream
  -> Courier Assignment
  -> Route Optimization
  -> ETA
  -> Promise Engine
  -> SLA Monitoring
  -> Zone Validation
  -> Tracking
  -> Exception Management
```

## 9. Batching & Route Simulation

Before adding a Mission to an existing Route, Mono should use routing data to simulate the revised sequence of stops.

Example:

```
Courier
  -> Pickup A
  -> Pickup B
  -> Dropoff 1
  -> Dropoff 2
  -> Dropoff 3
```

The new Route may be accepted only if all hard constraints remain valid, including committed delivery promises and pickup rules.

## 10. Build vs Buy

Mono will not build a map dataset or navigation engine from zero. It will use OSM data with a self-hosted Valhalla routing-engine candidate behind the provider-neutral Maps Gateway for server-side route and distance-matrix estimates.

For map display, Mono will build and host OSM-derived vector tiles itself, using Planetiler to create versioned MBTiles and an owner-operated tile service to serve them to MapLibre clients. This is separate from Valhalla. Tile hosting still requires infrastructure, bandwidth, data refresh, monitoring, attribution, and geographic validation. Do not use the public OSM tile server as a production dependency.

Geocoding, live traffic, and launching an external mobile navigation app remain separate capabilities and decisions. The business and orchestration core must remain independent from the underlying map and routing implementations.
