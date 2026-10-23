# ADR-0012 — API contract: versioning, errors, pagination, idempotency

- **Status:** Accepted
- **Date:** 2026-10-23
- **Deciders:** Architecture, Integrations

## Context

Two very different consumers (browser SPA and enterprise backends) share one API. Integrations
need stable contracts, safe retries, and clear errors. The source requires versioned endpoints,
idempotency for retried operations, and non-revealing error responses.

## Decision

- **Versioning:** path prefix `/v1`. Additive changes (new optional fields/endpoints, new enum
  values that clients must tolerate) are non-breaking. Breaking changes require `/v2` and a
  deprecation window; `/v1` returns `Deprecation` and `Sunset` headers during the window.
- **Media type:** JSON (`application/json`), errors `application/problem+json` (RFC 9457), binary
  content `application/octet-stream`.
- **Errors:** fields `type`, `title`, `status`, `detail`, `instance`, `code`, `traceId`,
  `retryable`; validation adds `errors[]`. Codes are stable and documented in the error catalog.
  Security-sensitive resources return 404 for both missing and unauthorized.
- **Pagination:** cursor-based (`limit` 1–100, default 25; opaque `cursor`; `nextCursor`,
  `hasMore`). Sort syntax `sort=-created_at,name`; supported fields are enumerated per endpoint.
- **Concurrency:** `ETag` on file metadata; `If-Match` required for PATCH; 412 on mismatch.
- **Idempotency:** `Idempotency-Key` (8–128 printable chars) on POST that create resources or
  trigger side effects; scoped per integration; 24 h retention; body hash mismatch → 422.
- **Rate limits:** headers as in ADR-0011; 429 with `Retry-After`.
- **Time:** RFC 3339 UTC (`...Z`). Sizes in bytes. IDs as UUID strings.
- **OpenAPI 3.1** is the contract artifact (`docs/04-api/openapi/files-api.v1.yaml`); contract
  tests fail the build if implementation responses diverge.

## Consequences

- Clients must tolerate additive changes and unknown enum values (documented).
- Cursor pagination prevents offset drift but cannot jump to page N; acceptable for file lists.
- The API surface is shared by portal and integrations but separated by route prefix and
  authorization policies, limiting blast radius and simplifying rate limits.
- Every new error code must be added to the catalog and covered by a test.

## Alternatives considered

- Header-based versioning: rejected (harder to cache/route, less explicit).
- Offset pagination: rejected (drift on inserts/deletes; expensive at scale).
- GraphQL: rejected (not needed; file streaming and caching are simpler with REST).
- Returning 403 for missing resources: rejected to avoid existence oracles (except admin routes
  where the caller is already authorized).
