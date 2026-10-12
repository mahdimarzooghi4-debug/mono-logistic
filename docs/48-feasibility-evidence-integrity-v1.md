# Feasibility Evidence Integrity v1

## Contract

A criterion result carries an integrity envelope containing an evidence identifier, observation timestamp, source ID, source attestation, source-payload digest, and final SHA-256 digest. The source-payload digest is computed over a canonical representation of the criterion, status, sorted reason codes, evidence identifier, UTC observation timestamp, and source ID; it excludes the attestation so the source can sign it. The final digest binds the source-payload digest and attestation to the full envelope.

At the Promise application boundary, an ACCEPTED result must have one valid envelope for each required E7.1 criterion. An ALTERNATIVE_PROMISE must have valid envelopes for both the requested-time assessment and the alternative-time assessment. Missing, duplicated, malformed, or changed result envelopes block persistence.

## Meaning and limits

The digest detects changes to a criterion result after its envelope is produced. It is not a digital signature and does not authenticate the caller, source system, or evidence collection process. A caller able to create an assertion can also compute its digest.

Sprint 063 added source verification as a fail-closed Promise gate. Sprint 090 defines a provider-neutral HMAC-SHA256 v1 attestation and a configuration-backed verifier. It remains disabled by default and still requires explicit customer/source/scope approval, secret provisioning, and freshness policy before it can verify any source. Route simulation, ETA, and safety-buffer calculation remain future work.
