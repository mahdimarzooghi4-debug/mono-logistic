# Mono Technical Stack and Internal AI ADR v1

- Status: Accepted
- Accepted on: 2026-10-10
- Scope: Mono Phase 1 / Hana urban-delivery MVP
- Supersedes: None

## Context

Mono is an independent logistics technology and operations business. Hana is its first customer. The implementation must support transactional delivery operations, geospatial data, an RTL web console, and an internal path to train and run predictive models.

The MVP remains governed by `docs/19-mvp-scope-v1.md`. In particular, AI dispatch is not an MVP requirement. Runtime decisions continue to follow `docs/14-runtime-decision-architecture-v1.md`: hard constraints determine eligibility; optimization ranks only eligible candidates; decisions are committed with evidence.

## Decision

### Application architecture

- Start with a modular monolith for the transactional logistics core. Preserve module boundaries and defer independently deployed services until measured operational needs justify them.
- Implement the core API and domain modules with ASP.NET Core on .NET 10 LTS.
- Implement the browser-based operations console with React and TypeScript, including Persian RTL support and the approved Figma design system.
- Use PostgreSQL 18 as the primary transactional database and PostGIS for geospatial types, queries, and indexes.
- Keep map and routing providers behind Mono-owned gateway interfaces. The provider selection is a separate decision.
- Do not introduce Redis, a message broker, or independently deployed services until the corresponding Phase A/B requirements and deployment design are specified.

### Internal AI boundary

- AI training and inference are Mono-owned workloads implemented in Python and run within Mono-controlled infrastructure.
- No external model API, hosted inference endpoint, external training service, or outbound transfer of operational data is allowed for AI training or inference.
- Existing model implementations or model families may be used, subject to license and security review. Model selection is a later technical decision based on a Mono dataset and measured evaluation.
- Training data must come from recorded Mono operational events and outcomes. Do not treat Figma sample values, synthetic examples, or unverified external data as operational truth.
- The first candidate use is ETA estimation and SLA-risk prediction. This work may begin as offline training and shadow evaluation; it does not make or commit dispatch, feasibility, promise, pricing, settlement, or safety decisions.
- Until a model has sufficient representative data and passes a separately approved evaluation gate, deterministic baselines remain authoritative.
- Persist model version, training-data lineage, evaluation results, prediction timestamp, and prediction inputs needed for audit. Do not overwrite committed runtime decisions with a new prediction.

## Consequences

- The transactional domain remains in .NET; Python is isolated to internal model development and execution.
- Internal model communication may use a private, authenticated interface, but it must not be exposed as a public API or proxy to an external model service.
- Event and outcome capture must be designed before model training so predictions can later be compared with actual outcomes.
- The first release can operate without an AI model. A model can be introduced behind a feature flag after data readiness and evaluation.
- This decision does not approve AI dispatch, automatic production training, automatic model promotion, or changes to Mono's hard constraints.

## Decisions still open

- Courier application framework.
- Courier identity provider and verified mobile/account-binding workflow. Sprint 116 adds a configurable, fail-closed OIDC/JWT validation boundary; no authority or audience is configured.
- Map and routing provider.
- Queue/broker and cache selection.
- Exact Python and ML library versions.
- Model family, training cadence, evaluation metrics, and admission thresholds.
- Hosting and deployment provider.

These choices require their own technical decisions before implementation depends on them.

## Implementation order

1. Establish repository structure, local development, formatting, tests, and CI for the selected stack.
2. Implement tenant/customer configuration and policy foundations.
3. Implement delivery request, mission lifecycle, feasibility, promise, and pricing using deterministic rules.
4. Record operational events and outcomes required for future ETA/SLA-risk evaluation.
5. Build the internal AI training/evaluation path after the dataset is representative; keep predictions advisory until separately approved.

## References

- [Microsoft .NET releases and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- [PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/)
- [PostGIS documentation](https://postgis.net/documentation/)
- [Mono Runtime Decision Architecture](14-runtime-decision-architecture-v1.md)
- [Mono MVP Scope](19-mvp-scope-v1.md)
- [Mono Deployment & Infrastructure Architecture](17-deployment-infrastructure-architecture-v1.md)
