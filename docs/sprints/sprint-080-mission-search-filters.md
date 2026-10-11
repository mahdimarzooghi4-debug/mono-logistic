# Sprint 080 — Operations Mission search filters

## Goal

Make the Mono operations Mission lookup useful for Hana's order references while keeping results bounded, repeatable, and privacy-limited.

## Changes

- Search by case-insensitive prefix across Mission ID, Delivery Request ID, or Delivery Request external reference.
- Add inclusive creation-time range filters beside the existing status filter.
- Validate search input and require the cursor record to match every active filter.
- Add a PostgreSQL prefix index for external references and exercise search, paging, detail, and timeline against PostgreSQL.
- Keep the external reference out of the operations response DTO.

## Security and scope

All routes retain the MFA-gated Mono administrator policy. The lookup is read-only, accepts no customer ID, and does not return customer-provided external references. No Mission lifecycle transitions, Stage, or deployment are included.

## Validation

CI will run the complete PostgreSQL-backed API test suite on this Draft PR.
