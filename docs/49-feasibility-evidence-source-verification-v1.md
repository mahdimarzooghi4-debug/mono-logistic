# Feasibility Evidence Source Verification v1

## Purpose

Bind each E7.1 criterion result to a claimed source and require a source verifier before a Promise may be persisted.

## Envelope

The integrity envelope carries:

- evidence ID and observation timestamp;
- source ID;
- opaque source attestation; and
- SHA-256 digest over the criterion result and all envelope fields.

Including source fields in the digest detects later changes to those fields. The digest does not prove that a source created the attestation.

## Verification boundary

`IFeasibilityEvidenceSourceVerifier` receives the authenticated customer ID, criterion, and integrity envelope. It must return `Verified` before commit continues. Any other result blocks persistence. The customer ID lets an eventual implementation scope trusted source registration to the requesting tenant.

The registered default, `UnconfiguredFeasibilityEvidenceSourceVerifier`, returns `VerifierNotConfigured` for every criterion. Therefore, Promise commits through this application service remain blocked until an approved verifier is configured. This is intentional fail-closed behavior.

## Decisions still required

Mono has not selected:

- which systems or operators may provide each criterion;
- how sources are registered and scoped to a customer;
- the attestation format or cryptographic verification method;
- key storage, rotation, or revocation rules; or
- evidence freshness and retention rules.

No source ID or opaque attestation is trusted merely because it is present. No external provider or signature algorithm is assumed.
