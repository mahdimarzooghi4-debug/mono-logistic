# Mono Policy Draft Authoring API

All routes are internal Mono operations endpoints. They require the Mono administrator role and issuer-asserted MFA. Customer API keys cannot authorize them.

This sprint creates and inspects policy drafts only. It does not activate, retire, evaluate, resolve, or apply policies to delivery requests or Missions. A policy definition is stored as a JSON object; its family-specific business semantics are not interpreted here.

## List policy drafts

`GET /operations/v1/policies?page_size=50&cursor=pol_...`

Returns policy identity, scope, creator, latest version number, and latest status in stable policy-ID order. The default page size is 50; accepted values are 1–100. Use `nextCursor` as the next request's `cursor`. A malformed cursor or page size returns `400 INVALID_REQUEST`.

## Create a policy and its first Draft version

`POST /operations/v1/policies`

```json
{
  "policyType": "COVERAGE_ZONE",
  "scope": "CUSTOMER",
  "scopeId": "cus_...",
  "enforcementType": "HARD",
  "priority": 10,
  "effectiveFrom": null,
  "effectiveTo": null,
  "definition": {
    "maxDistanceKm": 5
  },
  "reason": "Initial draft for review"
}
```

The response is `201 Created` with a generated `pol_...` ID and version 1 in `DRAFT` status. Supported scopes are `PLATFORM`, `SERVICE`, `CUSTOMER`, `BRANCH`, `ZONE`, `CONTRACT`, `MISSION`, and `RUNTIME`. Platform scope omits `scopeId`; all other scopes require a non-empty scope ID. Scope ownership is not resolved against domain tables in this draft-authoring step.

Policy types are normalized to uppercase identifiers. Enforcement types are `HARD`, `OPTIMIZATION`, `COMMERCIAL`, `CONFIGURATION`, `CONTRACTUAL`, `WORKFLOW`, `FINANCIAL`, and `RISK`. Definitions must be JSON objects. If both effective dates are provided, `effectiveTo` must be later than `effectiveFrom`.

## Inspect a policy and its versions

`GET /operations/v1/policies/{policyId}`

Returns the policy metadata and its versions in ascending version order, including each immutable Draft definition. Unknown IDs return `404 POLICY_NOT_FOUND`; malformed IDs return `400 INVALID_REQUEST`.

## Create a new Draft version

`POST /operations/v1/policies/{policyId}/versions`

The request supplies a complete new version definition, enforcement type, priority, optional effective dates, and a reason. The server locks the policy identity, assigns the next version number, and creates the version in `DRAFT`. Existing versions are preserved. Unknown IDs return `404 POLICY_NOT_FOUND`.

## Inspect audit history

`GET /operations/v1/policies/{policyId}/audit`

Returns append-only records for policy creation and Draft version creation, including the policy snapshot, actor, reason, and timestamp. Unknown IDs return `404 POLICY_NOT_FOUND`.

## Errors

- `400 INVALID_REQUEST`: invalid policy fields, definition, dates, reason, page size, or cursor.
- `404 POLICY_NOT_FOUND`: well-formed policy ID is unknown.
- `401` / `403`: missing administrator authentication or insufficient role/MFA.
