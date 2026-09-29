# Mono Security Architecture v1

## 1. Purpose

Mono is a multi-tenant B2B logistics platform handling operational, financial, identity, location and customer data.

Security must protect:

- customer data,
- courier identity,
- live location,
- financial balances,
- API credentials,
- operational controls,
- policy/configuration changes,
- evidence and audit records.

## 2. Security Principles

1. Least privilege by default.
2. Tenant isolation is mandatory.
3. Authentication and authorization are separate concerns.
4. Sensitive actions require explicit audit.
5. Secrets never belong in source code.
6. Financial and policy mutations require stronger controls.
7. Security boundaries must not depend only on UI restrictions.
8. Operational location access must be purpose-limited.
9. External integrations must be authenticated and replay-resistant.
10. Manual overrides must always be attributable.

## 3. Identity Domains

Mono should distinguish:

- Customer organization
- Customer users
- Mono operations staff
- Mono administrators
- Courier users
- Provider users
- Service accounts
- Machine-to-machine API clients

These identities must not share one flat permission model.

## 4. Authentication

Recommended:

### B2B API
- OAuth2 Client Credentials for enterprise integrations
- API keys only for controlled/simple use cases
- credential rotation
- key expiry
- revocation

### Dashboard
- secure session authentication
- MFA for privileged roles
- device/session management
- passwordless or enterprise SSO where available

### Courier App
- verified mobile identity
- device/session binding
- refresh token rotation
- risk-based reauthentication

## 5. Authorization

Use role + scope + tenant context.

Example roles:

- CUSTOMER_OWNER
- CUSTOMER_ADMIN
- CUSTOMER_OPERATOR
- CUSTOMER_FINANCE
- MONO_OPERATOR
- MONO_FINANCE
- MONO_SUPPORT
- MONO_ADMIN
- COURIER
- PROVIDER_OPERATOR

Authorization should evaluate:

```
actor
+ tenant
+ role
+ resource
+ action
+ policy scope
```

## 6. Tenant Isolation

Every customer-scoped request must resolve a trusted `customer_id` from authenticated context.

Never trust customer_id supplied only in request body.

Controls:

- tenant-aware repository layer
- authorization middleware
- row-level security where useful
- scoped cache keys
- scoped object storage paths
- tenant ID in audit/event records

## 7. API Security

External API requirements:

- HTTPS only
- rate limiting
- request size limits
- schema validation
- idempotency keys
- request IDs
- abuse detection
- auth scope validation

Sensitive endpoints should support stricter limits.

## 8. Webhook Security

Recommended:

- HTTPS only
- HMAC signature
- timestamp header
- event ID
- replay window
- secret rotation
- retry with bounded backoff

Headers:

```
Mono-Event-Id
Mono-Event-Type
Mono-Timestamp
Mono-Signature
```

## 9. Secrets Management

Secrets include:

- API keys
- OAuth client secrets
- database credentials
- map provider keys
- webhook secrets
- payment credentials
- encryption keys

Rules:

- store in managed secret store
- never commit to Git
- rotate periodically
- separate per environment
- audit access
- revoke immediately on compromise

## 10. Data Encryption

### In Transit
TLS for:

- API
- webhooks
- internal service calls
- database connections
- broker connections

### At Rest
Encrypt:

- databases
- object storage
- backups
- sensitive document storage

Higher sensitivity data may require field-level encryption.

## 11. Sensitive Data Classes

Suggested classification:

### Restricted
- financial credentials
- identity documents
- security secrets
- KYC records

### Confidential
- customer order metadata
- courier personal data
- recipient contact data
- live/historical GPS
- contract pricing

### Internal
- operational metrics
- route planning
- provider scores

### Public
- explicitly published documentation/content only

## 12. Location Privacy

Courier tracking is operational, not general surveillance.

Rules:

- no unnecessary 24/7 high-frequency tracking
- mission-aware collection
- role-limited access
- retention policy
- customer only sees contracted fields
- location access must be auditable for privileged staff

## 13. Courier Privacy

Customer should not automatically receive:

- national ID
- personal bank data
- full identity documents
- private contact details beyond required operational data

Expose only delivery-required/public courier profile fields.

## 14. Financial Security

Financial controls:

- immutable ledger entries
- dual-control/manual approval for high-risk adjustments
- withdrawal rate limits
- payout destination verification
- anti-duplicate posting constraints
- transaction idempotency
- reconciliation
- audit of every manual adjustment

## 15. Policy & Configuration Security

Policy changes can alter:

- pricing
- SLA
- eligibility
- financial outcomes
- security requirements

Therefore:

- version every change
- record actor
- support draft/active states
- optionally require approval for sensitive policy families
- preserve old versions
- prevent retroactive silent mutation

## 16. Operations Console Security

Operations actions such as:

- re-dispatch
- manual cancel
- settlement correction
- force exception
- courier suspension

must require explicit permission and create audit records.

High-risk actions may require reason entry or secondary approval.

## 17. Fraud & Abuse Controls

Initial detection areas:

- GPS spoofing
- duplicate courier identity
- account sharing
- repeated fake delivery
- impossible travel
- suspicious cancellation patterns
- wallet/payout abuse
- repeated failed OTP/signature if enabled
- collusion patterns
- webhook abuse
- credential stuffing

## 18. Device Trust

For courier operations, future controls may include:

- device registration
- device fingerprint
- rooted/jailbroken device signals
- emulator detection
- app attestation
- suspicious location-source detection

These should be risk signals, not automatically hard-coded bans without policy.

## 19. Audit Architecture

Security-sensitive audit record:

```
audit_id
actor_type
actor_id
customer_id
action
resource_type
resource_id
reason
request_id
ip_address
device_id
occurred_at
before_summary
after_summary
```

Audit records should be append-only.

## 20. Security Events

Examples:

- auth.login_failed
- auth.credential_revoked
- courier.device_changed
- security.gps_spoof_suspected
- security.rate_limit_triggered
- policy.sensitive_change
- finance.manual_adjustment
- admin.role_changed

## 21. Environment Separation

At minimum:

- local/development
- staging
- production

Rules:

- separate credentials
- separate databases
- separate secrets
- no production secrets in staging
- production access restricted

## 22. Dependency Security

Requirements:

- dependency scanning
- vulnerability alerts
- lockfiles
- update policy
- container image scanning
- OS/package patching
- SBOM where practical

## 23. Secure Development Lifecycle

Mono development workflow should include:

```
Code
-> Review
-> Automated Tests
-> Security Checks
-> Stage
-> QA
-> Release Approval
-> Production
```

Security gates include:

- secret scan
- dependency scan
- static analysis
- migration review
- auth/permission tests
- API contract tests

## 24. Incident Response

Mono should define:

- detection
- severity
- containment
- credential revocation
- customer impact assessment
- recovery
- post-incident review

Production incidents affecting finance, identity or tenant isolation require highest priority handling.

## 25. Backup Security

Backups must be:

- encrypted
- access-controlled
- monitored
- periodically restore-tested

Backup access must be more restricted than normal application access.

## 26. Security SLOs / KPIs

Track:

- failed auth rate
- suspicious login rate
- credential rotation compliance
- webhook signature failures
- unauthorized access attempts
- security alert resolution time
- manual finance adjustment rate
- tenant isolation test coverage

## 27. Core Principle

> **Security is part of the logistics operating model, not a layer added after implementation.**
