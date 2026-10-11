# Eligible Modes Feasibility Criterion v1

## Purpose

Translate Mono's existing effective-policy mode preview into the E7.1 `Eligible modes` evidence result.

## Contract

- At least one policy-eligible candidate mode returns `PASS`.
- No eligible mode with complete candidate evaluations returns `FAIL` with `NO_ELIGIBLE_MODE` and the preview's distinct exclusion reasons.
- A missing capability policy, unsupported policy definition, missing policy fact, or absent candidate evidence returns `UNRESOLVED`.
- Candidate IDs and reason codes must be canonical; the eligible list and aggregate reasons must match candidate evidence. Inconsistent input is rejected.

## Boundary

This is an adapter over the existing administrator preview. It does not query live courier/vehicle availability, choose a mode for a delivery, evaluate the other seven E7.1 criteria, persist decision evidence, call an external service, or create a Mission or Promise. It does not wire the preview into delivery intake.
