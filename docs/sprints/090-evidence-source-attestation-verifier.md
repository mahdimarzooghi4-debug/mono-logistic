# Sprint 090 — Evidence source attestation verifier

## Goal

Add a provider-neutral cryptographic verification implementation for Hana distance evidence and Promise feasibility evidence while preserving fail-closed defaults.

## Changes

- Define HMAC-SHA256 attestation v1, with tenant, source, evidence, criterion scope, observation time, and source-payload digest binding.
- Add a non-circular source-payload digest to the feasibility integrity envelope and a canonical source-payload digest helper for Hana distance evidence.
- Add a configuration-backed trust registry verifier with tenant/source/scope/key matching, key rotation IDs, constant-time signature comparison, required freshness limits, and explicit global opt-in.
- Keep verification disabled by default; no provider, customer source, secret, external API, or server configuration is selected or supplied.
- Add contract tests for verified evidence, tenant/scope isolation, stale evidence, duplicate keys, and fail-closed configuration.
- Update the source-verification, evidence-integrity, Hana-pricing, and product-backlog documentation.

## Acceptance

- A signature is valid only for the exact customer, source, scope, evidence ID, timestamp, and canonical source-payload digest.
- Quote and Promise remain blocked unless runtime source configuration is explicitly enabled and complete.
- Missing, duplicate, malformed, disabled, or out-of-scope source configuration and stale or future-dated evidence never returns Verified.
- No key material is committed to the repository.

## Out of scope

- Selecting or approving an actual maps/GPS/provider source.
- Provisioning real secrets or enabling the verifier in a runtime environment.
- Connecting a network provider, Stage work, deployment, or production rollout.
