# Sprint 227 — Immutable CI Action Pins

## Goal

Make CI's GitHub Actions dependencies immutable and prevent tag-based action references from returning silently.

## Delivered

- Pin checkout and Node setup actions in the Operations Console workflow to full commit SHAs.
- Pin the Node setup action in Mono CI to the same full SHA already used by the Merchant Portal workflow.
- Add a repository test that scans every workflow and fails when an action reference is not pinned to a 40-character commit SHA.
- Record the capability in the product backlog.

## Verification

Mono CI checks the pinning rule before build and test steps. The Operations Console workflow runs on changes to its source and workflow. No deployment, Render resource, secret, or production behavior changes.

## Boundary

The SHA values reuse exact commits already referenced in the repository's Merchant Portal and Mono CI workflows. This sprint changes only CI dependency references and their regression guard.
