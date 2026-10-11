# Sprint 075 — Promise API contract coverage

## Goal

Verify the public Promise commit endpoint's existing success, failure, and authorization behavior end to end.

## Coverage

- Keep successful commit, exact retry during verifier outage, tampered-evidence rejection, changed-input conflict, and one-version persistence coverage.
- Verify `PROMISE_UNRESOLVED`, `PROMISE_REJECTED`, `PROMISE_PROPOSAL_MISMATCH`, and `PROMISE_EVIDENCE_UNVERIFIED` status/error mappings.
- Verify a tenant API key with `delivery-requests.read` but without `delivery-requests.write` cannot commit a Promise.
- Confirm failed decisions do not create another Promise version and do not trigger source verification before the existing decision/evidence gates allow it.

This sprint adds no feasibility or authorization rules. It validates the contract already implemented by the application service and endpoint. No Stage or deployment is included.
