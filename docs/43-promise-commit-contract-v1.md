# Promise Commit Contract v1

## Purpose

Define the structural input Mono must validate before a Promise can be committed after feasibility evaluation.

## Proposal fields

A proposal includes:

- a decision identifier and positive Promise version;
- evaluation time and committed delivery time;
- an ACCEPTED or ALTERNATIVE_PROMISE feasibility outcome;
- an explicit safety-buffer duration and the policy version that supplied it;
- the decision reason codes and the applicable policy-version map.

The delivery time must be later than evaluation time. Safety buffer must be non-negative. REJECTED is not a Promise and cannot be proposed for commitment.

## Boundary

This contract validates data shape and consistency only. It does not calculate ETA or the buffer, assert that a supplied value is operationally safe, verify feasibility evidence or authorization, compare versions against stored history, persist a Promise, write an outbox event, or create a Mission. A validated proposal is not authoritative until a future commit operation persists it atomically with its decision evidence.
