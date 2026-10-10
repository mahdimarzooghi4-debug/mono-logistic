# Mono Policy Draft Authoring API

All routes are internal Mono operations endpoints. They require the Mono administrator role and issuer-asserted MFA. Customer API keys cannot authorize them.

Policy authoring stores JSON definitions. Lifecycle changes only change registry status and append audit history. The evaluation preview below interprets only the explicitly supported HARD constraint subset; other definitions fail closed.

## Create a policy and its first Draft version

`POST /operations/v1/policies`

```json
{
  "policyType": "VEHICLE_CAPACITY",
  "scope": "CUSTOMER",
  "scopeId": "cus_...",
  "enforcementType": "HARD",
  "priority": 10,
  "effectiveFrom": null,
  "effectiveTo": null,
  "definition": {
    "constraints": [
      {
        "fact": "weight_kg",
        "operator": "LTE",
        "value": 25,
        "reasonCode": "MAX_WEIGHT_EXCEEDED"
      }
    ]
  },
  "reason": "Initial draft for review"
}
```

## Hard policy evaluation preview

`POST /operations/v1/policies/evaluate`

Requires an MFA-gated Mono administrator. This is an operations preview only; it does not alter delivery requests or Missions.

Request body:

```json
{
  "serviceId": "urban-delivery",
  "customerId": "cus_...",
  "contractId": null,
  "branchId": "branch_...",
  "zoneIds": ["zone_..."],
  "missionId": null,
  "runtimeId": null,
  "requestedAt": null,
  "facts": {
    "weight_kg": 20,
    "distance_km": 4.5,
    "mode": "BIKE"
  }
}
```

Each active, effective HARD policy definition must contain a non-empty `constraints` array (up to 100 entries). Each constraint has exactly four fields: `fact` (a lowercase snake_case top-level fact name), `operator`, `value`, and `reasonCode`.

Supported operators are `EQ`, `NE`, `IN`, `NOT_IN`, `GT`, `GTE`, `LT`, and `LTE`. Values must be JSON scalars; `IN` and `NOT_IN` take an array of up to 100 scalars. Numeric and string ordering comparisons require matching types. Equality and membership use exact JSON scalar type and ordinal string matching. Facts are top-level only, with at most 100 fields.

A constraint passes when its fact satisfies its operator against the configured value. Every constraint in a policy must pass. A failure excludes the candidate and returns the configured reason code. Missing facts return `POLICY_FACT_MISSING`. Invalid/unsupported policy definitions or incompatible comparison types return `POLICY_DEFINITION_UNSUPPORTED`; these also fail closed. The response includes each evaluated policy ID and version, overall pass/fail, exclusion, and distinct reason codes. Non-HARD policies are counted but not evaluated.

The preview resolves active and effective versions for the supplied context and requested time using the same precedence ordering as `GET /operations/v1/policies/effective`. It does not merge same-family policies, resolve contradictions, apply optimizer behavior, persist a decision record, or execute a delivery decision.

## Lifecycle and inspection routes

The existing operations endpoints create immutable Draft versions, activate or retire versions transactionally, inspect versions and audit history, and resolve effective policy versions. Activation and retirement remain configuration operations; they do not apply policies to customer traffic.
