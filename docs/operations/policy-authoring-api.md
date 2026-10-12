# Mono Policy Draft Authoring API

All routes are internal Mono operations endpoints. They require the Mono administrator role and issuer-asserted MFA. Customer API keys cannot authorize them.

Policy authoring stores JSON definitions without interpreting family-specific business semantics. Lifecycle changes require an MFA-gated Mono administrator. Activation and retirement only change registry status and append audit history; they do not resolve or apply policies to delivery requests or Missions.

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

## Activate a Draft version

`POST /operations/v1/policies/{policyId}/versions/{versionNumber}/activate`

```json
{ "reason": "Approved after operations review" }
```

Only a `DRAFT` version can be activated. If another version is active under the same policy identity, it becomes `INACTIVE` in the same database transaction. A version whose `effectiveTo` is in the past cannot be activated. PostgreSQL enforces at most one `ACTIVE` version per policy. Activation appends `VERSION_DEACTIVATED` for the prior version when present and `VERSION_ACTIVATED` for the new version.

## Retire a version

`POST /operations/v1/policies/{policyId}/versions/{versionNumber}/retire`

Only an `ACTIVE` or `INACTIVE` version can be retired. Retirement is permanent; definitions and audit entries are preserved. Drafts cannot be retired directly.

Both operations require a non-empty reason of at most 1000 characters. Status changes and audit entries commit atomically. A disallowed transition returns `409 POLICY_VERSION_STATE_CONFLICT`; an unknown policy or version returns `404`.

## Resolve effective policy versions

`GET /operations/v1/policies/effective`

The operations preview resolves registry entries for an explicit context. It requires an MFA-gated Mono administrator. Optional query parameters are `service_id`, `customer_id`, `contract_id`, `branch_id`, repeated `zone_id`, `mission_id`, `runtime_id`, and `requested_at`. When omitted, `requested_at` is the current UTC time. Scope IDs are matched exactly after trimming; at most 20 zone IDs are accepted.

Only `ACTIVE` versions effective at `requested_at` are returned. Platform policies always match. Other scopes match only when the corresponding context ID is supplied. Results are ordered by scope tier (Platform, Service, Customer/Contract, Branch/Zone, Mission/Runtime), then descending priority and stable policy ID. Each result includes the definition, version number, enforcement type, dates, and precedence tier.

This route identifies and orders applicable policy versions. It does not merge definitions, enforce override rules, evaluate conditions, make delivery decisions, or apply financial effects. Runtime consumers and policy-family validation remain separate work.

## Inspect audit history

`GET /operations/v1/policies/{policyId}/audit`

Returns append-only records for policy creation, Draft version creation, activation, deactivation, and retirement, including the policy snapshot, actor, reason, and timestamp. Unknown IDs return `404 POLICY_NOT_FOUND`.

## Errors

- `400 INVALID_REQUEST`: invalid policy fields, definition, dates, reason, page size, or cursor.
- `404 POLICY_NOT_FOUND`: well-formed policy ID is unknown.
- `401` / `403`: missing administrator authentication or insufficient role/MFA.
