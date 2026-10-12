# Quote Issuance Core v1

## Application boundary

`DeliveryQuoteIssuanceApplicationService.IssueInitialAsync` creates the first Quote version for a tenant-owned delivery request. It requires an explicit future `expires_at`, validates the distance evidence integrity digest, asks the source verifier for the authenticated customer, and calculates the fare with `HanaDistancePricingV1`. Clients cannot provide a fare or policy version.

Only a `Verified` source result reaches persistence. The default verifier returns `DISTANCE_SOURCE_VERIFIER_NOT_CONFIGURED`, so normal production configuration blocks issuance until a provider-specific verifier is approved and registered.

The Quote snapshot stores the evidence ID, observed time, distance, source ID, and digest as an internal audit reference. It does not store the opaque attestation payload. Customer read responses continue to omit this internal reference.

## Expiry and versions

The caller must provide an expiry later than the server's issue time. No default TTL, minimum duration, or maximum duration is imposed.

This capability creates version 1 only. The existing repository rejects conflicting version writes and locks the tenant-owned request row. Quote renewals and versions after the initial issue require a separate operation.

## Scope limits

This sprint adds the application service and DI wiring. It does not add a public HTTP write route, Idempotency-Key handling, renewal, provider integration, or a source attestation implementation. Those belong to follow-on work after trust configuration and API retry semantics are decided.
