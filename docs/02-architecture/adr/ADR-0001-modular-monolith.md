# ADR-0001 — Modular monolith with a separate worker

- **Status:** Accepted
- **Date:** 2026-10-23
- **Deciders:** Architecture

## Context

Files has a small, well-bounded domain: files, links, integrations, retention, and audit. The
source spec requires a C#/.NET API, container-first delivery, and background processing for
notifications/expiration/deletion. A microservice split would multiply operational work with no
demonstrated scaling need.

## Decision

Build one bounded application split into two deployable processes:

- `Files.Api` — synchronous HTTP surface (portal, integration, public links, admin).
- `Files.Worker` — scheduled jobs and outbox dispatch, no public ingress.

Both share `Files.Domain`, `Files.Application`, and `Files.Infrastructure`. The codebase is
organized by layers with feature folders inside Application; modules are not separately
deployable but keep explicit boundaries (no cross-feature table access).

## Consequences

- Single database transaction for metadata + outbox; simpler consistency.
- API and worker scale independently (worker usually one replica).
- We must guard against a "big ball of mud": dependency rules are enforced in review, folder
  conventions, and architecture tests (NetArchTest).
- Future extraction of a module (e.g., notifications) is possible without rewriting the domain.

## Alternatives considered

- Microservices per capability: rejected (operational cost, distributed transactions, no need).
- Single process including jobs: rejected (job load competes with request latency; separate
  worker gives clean scaling and failure isolation).
