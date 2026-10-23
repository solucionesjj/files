# ADR-0013 — Angular standalone + Bootstrap for the portal

- **Status:** Accepted
- **Date:** 2026-10-23
- **Deciders:** Frontend, Architecture

## Context

The source mandates Angular with "standard latest Bootstrap". The portal is a small internal SPA:
list/upload/detail, link management, admin. The team needs fast delivery, accessibility, and
straightforward Entra integration.

## Decision

- Angular LTS with standalone components, functional guards/interceptors, signals for local
  state, and lazy-loaded feature routes.
- Bootstrap 5.3 for layout and components; Bootstrap Icons; SCSS with a small theme override.
- `@azure/msal-angular` with Authorization Code + PKCE, `sessionStorage`, silent token renewal.
- No NgRx in v1; feature services + signals. No UI component library beyond Bootstrap.
- Runtime configuration via `/assets/config.json` (no secrets), not build-time environment files
  for values that differ per deployment.
- Spanish UI via `@angular/localize`; English identifiers/docs.
- WCAG 2.1 AA and keyboard-first flows as acceptance criteria, not polish.

## Consequences

- Minimal dependencies and a familiar look; Bootstrap's JS is kept to modals/dropdowns needed.
- Signals reduce boilerplate but require discipline for async state; RxJS remains for HTTP.
- MSAL's interceptor must be scoped to the API origin to avoid leaking tokens to S3/Graph.
- A future design system or SSR can be added without changing the API contract.

## Alternatives considered

- React/Vue: rejected (source mandates Angular).
- Angular Material: rejected (spec says Bootstrap; extra bundle and visual divergence).
- NgRx: rejected for the current scope; revisit if cross-feature state grows.
- Server-side rendering: rejected (authenticated internal tool; no SEO need).
