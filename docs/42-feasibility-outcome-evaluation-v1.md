# Feasibility Outcome Evaluation v1

## Approved outcome rule

- ACCEPTED: the requested delivery time passes all eight E7.1 criteria.
- ALTERNATIVE_PROMISE: the requested time is blocked, and an explicit alternative proposal passes every E7.1 criterion.
- REJECTED: the requested time is blocked and a complete alternative search finds no feasible alternative.
- UNRESOLVED: requested evidence or alternative-search evidence is missing, incomplete, unavailable, or inconclusive.

## Evidence contract

The outcome evaluator consumes criterion results for the requested time, the requested time itself, and—when required—an alternative-search result. A feasible alternative carries its proposed delivery time and all eight passing E7.1 results. A complete negative search carries reason codes and no proposed alternative.

## Boundary

This evaluator does not search or rank alternative times, calculate ETA, choose an SLA, simulate routes, verify the source or completeness assertion, persist a decision, commit a Promise, or create a Mission. It returns an evaluation result only. Promise commitment remains a separate future operation after explicit confirmation and applicable checks.
