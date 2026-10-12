# Feasibility Evidence Integrity v1

## Contract

A criterion result may carry an integrity envelope containing an evidence identifier, observation timestamp, and SHA-256 digest. The digest is computed over a canonical representation of the criterion, status, sorted reason codes, evidence identifier, and UTC observation timestamp.

At the Promise application boundary, an ACCEPTED result must have one valid envelope for each required E7.1 criterion. An ALTERNATIVE_PROMISE must have valid envelopes for both the requested-time assessment and the alternative-time assessment. Missing, duplicated, malformed, or changed result envelopes block persistence.

## Meaning and limits

The digest detects changes to a criterion result after its envelope is produced. It is not a digital signature and does not authenticate the caller, source system, or evidence collection process. A caller able to create an assertion can also compute its digest.

Sprint 063 adds source ID and opaque source-attestation fields to the envelope and requires an `IFeasibilityEvidenceSourceVerifier` before Promise persistence. The default verifier rejects every result because trusted source registration and a source-specific verification method have not been selected. These fields alone do not establish that a criterion assertion is accurate, complete, fresh, or authorized. No external provider or key-management scheme is selected. Route simulation, ETA, and safety-buffer calculation remain future work.
