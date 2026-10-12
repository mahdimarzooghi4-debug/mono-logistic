# Hana Distance Pricing Evidence v1

## Evidence envelope

A pricing evidence envelope binds the measured distance in metres, evidence ID, observation time, source ID, and opaque source attestation to a canonical SHA-256 digest. Equivalent UTC offsets and decimal scales produce the same digest. Changes to any bound field invalidate it.

Distance must be between 0 and 5,000 metres, inclusive, matching the approved Hana Phase-1 tariff input range. A Quote issuer can use the distance in a valid envelope to calculate fare with `HanaDistancePricingV1`.

## Trust boundary

The final digest detects post-envelope mutation. It is not a signature and does not authenticate the source or prove that the distance is correct. `ComputeSourcePayloadSha256` hashes the canonical evidence ID, UTC observation time, distance, and source ID without the attestation. A source can then create a `mono-hmac-sha256.v1` attestation over that digest; the final digest binds the attestation into the evidence envelope.

`ConfiguredEvidenceSourceVerifier` checks an enabled tenant/source/key entry, the `hana-distance-pricing` scope, the HMAC signature, and explicitly configured freshness limits. Missing configuration remains fail-closed. No provider or credential is approved or configured by this change.

## Scope

This contract does not set Quote expiry, calculate a route, persist source evidence, or issue a Quote. Those steps depend on a trusted source verification decision and a separate Quote issuance capability.
