# Feasibility Evidence Source Verification v1

## Purpose

Authenticate the source and scope of evidence before a Promise is persisted. Source verification is independent from the SHA-256 integrity check: an integrity digest alone does not prove who supplied a criterion result.

## Attestation and payload hashes

`FeasibilityEvidenceIntegrity` carries the evidence ID, observation time, source ID, opaque attestation, `SourcePayloadSha256`, and final `PayloadSha256`.

`SourcePayloadSha256` is the canonical SHA-256 of the criterion, status, sorted reason codes, evidence ID, UTC observation time, and source ID. It excludes the attestation so a source can sign it without a circular hash. The final `PayloadSha256` binds that digest and the attestation together with the rest of the envelope.

The v1 attestation format is:

```text
mono-hmac-sha256.v1.<key-id>.<base64url-hmac-sha256>
```

The HMAC input is the UTF-8 encoding of these newline-separated values:

```text
mono-evidence-source-attestation-v1
<customer-id>
<source-id>
<scope>
<evidence-id>
<observed-at-utc-round-trip>
<source-payload-sha256>
```

Feasibility scopes are criterion-specific: `feasibility:Distance`, `feasibility:Coverage`, and the exact enum names for the other criteria. Hana distance pricing uses `hana-distance-pricing`. HMAC keys must contain at least 256 bits. The verifier compares signatures in constant time.

## Runtime trust registry

`ConfiguredEvidenceSourceVerifier` implements both Promise feasibility and Hana distance verification. It reads `Mono:EvidenceSources` runtime configuration. The global `Enabled` switch defaults to off. Each trusted source entry binds an exact customer ID, source ID, key ID, enabled state, allowed scopes, and freshness limits (`MaxEvidenceAgeSeconds` and `AllowedFutureClockSkewSeconds`). Missing, ambiguous, malformed, disabled, or out-of-scope configuration fails closed; stale or future-dated evidence is rejected.

Keys are Base64-encoded runtime secrets and are never stored in the repository or returned by an API. Supply them through the deployment secret mechanism when infrastructure is approved. Key IDs permit rotation; only an explicitly enabled matching key and scope can verify an attestation. No environment variables or credentials are configured by this change.

## Trust and rollout boundary

This implementation selects a provider-neutral cryptographic format. It does not approve a maps provider, courier feed, customer, source ID, or trust relationship. No source is trusted by default; without explicit runtime configuration, Quote and Promise writes remain blocked as before. Before enabling a source, Mono must approve the exact source/customer/scope and its evidence freshness policy, then provision a shared secret securely.

The following remain operational or business decisions:

- provider/operator approval for each criterion;
- source registration and key custody/rotation/revocation process;
- evidence retention and audit requirements; and
- appropriate freshness and clock-skew values for each source.

No external API is required for this verifier.
