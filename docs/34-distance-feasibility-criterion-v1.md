# Distance Feasibility Criterion v1

## Purpose

Evaluate the route-distance constraint as one isolated input to Mono's larger feasibility decision.

## Contract

The evaluator receives:

- route distance in metres, normally supplied by the provider-neutral Maps Gateway;
- a maximum distance in metres supplied by effective service configuration.

Results:

- `PASS` when route distance is at or below the configured maximum;
- `FAIL` with `MAX_DISTANCE_EXCEEDED` when route distance exceeds the maximum;
- `UNRESOLVED` with `ROUTE_DISTANCE_UNAVAILABLE` when no route distance is available.

Both a supplied route distance and the configured maximum must be finite and non-negative. A zero distance is valid. The evaluator does not choose the configured maximum. Hana's documented 5 km limit can be supplied as 5,000 metres by a future configuration integration.

## Decision boundary

This criterion is not a final feasibility, acceptance, rejection, or Promise decision. A pass only means the distance check passed; coverage, eligible modes and couriers, capacity, active routes, route insertion, and SLA feasibility still require evaluation. It does not call a map provider, resolve addresses, persist evidence, or create a Mission.
