# Sprint 083 — Operations Delivery Request inspection

## Goal

Let Mono operations locate and inspect Hana orders before and after Mission confirmation, using the same tenant-neutral operations surface.

## Changes

- Add MFA-gated, cross-customer Delivery Request search with case-insensitive prefix, status, and inclusive creation-time filters.
- Add stable keyset pagination and require cursor records to match every active filter.
- Add a detail read returning only request ID, customer, branch, external reference, status, and creation time.
- Exclude the stored payload, addresses, coordinates, and contact data from operations responses.
- Record search and detail access in a dedicated append-only audit table without storing search terms.
- Add request-search indexes, PostgreSQL integration coverage, and API/backlog documentation.

## Security and scope

Every route requires the existing Mono administrator policy with verified MFA. The read audit write completes before the response is returned. No Delivery Request mutation, Mission state change, Stage, or deployment is included.

## Validation

CI will run the full build, PostgreSQL-backed API tests, and API container image build on this Draft PR.
