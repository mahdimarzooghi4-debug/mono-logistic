# ADR 026 — Managed API Keys for the First B2B Integration

- Status: Accepted for Phase A
- Date: 2026-10-10
- Context: Mono is an independent logistics business; Hana is its first customer integration.

## Decision

Use managed per-customer API keys for Mono's first controlled system-to-system integration. Keep authentication provider-neutral so OAuth2 Client Credentials can be added as another scheme when enterprise integrations require it.

Send the key in `X-Mono-API-Key` with this shape:

```
mono_live_{16 lowercase hex key-id}_{64 lowercase hex secret}
```

Each key belongs to one customer and a server-issued set of scopes. The customer ID comes only from its credential record and becomes the authenticated `mono/customer_id` claim.

## Storage and validation

- Store only the SHA-256 hash of the complete high-entropy key; reveal the raw key once during provisioning.
- Look up credentials using a separate non-secret key ID.
- Reject malformed, unknown, expired, revoked, inactive-customer, and unscoped credentials with the same generic authentication failure.
- Compare hashes in fixed time.
- Never accept a customer ID from the API request or log the presented key.
- Support expiry and immediate revocation.
- Require HTTPS and add per-customer/key rate limits with protected business endpoints.
- Do not add a public key-issuance endpoint; provisioning remains an internal operational workflow.

## Consequences

This gives Hana a straightforward integration contract and keeps tenant identity server-controlled. It does not provide OAuth token exchange, delegated authorization, or richer federation. OAuth2 Client Credentials remains available for integrations that need those capabilities.

## Implementation boundary

The authentication handler, credential persistence, and internal command-line provisioning workflow are implemented across Sprints 004–006. Sprint 022 adds immediate administrator revocation with a transactionally recorded immutable audit event. The provisioner only issues keys for existing ACTIVE customers, accepts registered scopes, stores only a SHA-256 hash, requires an explicit expiry and interactive confirmation, and reveals the key once after successful storage. No public key-issuance endpoint, customer-facing provisioning flow, deployment, staging, or production change is included. The host must provide `ConnectionStrings:Mono` before a protected customer API route is enabled.
