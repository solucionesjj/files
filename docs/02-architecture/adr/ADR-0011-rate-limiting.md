# ADR-0011 — Hybrid rate limiting and abuse control

- **Status:** Accepted
- **Date:** 2026-10-23
- **Deciders:** Security, Architecture

## Context

Public download keys have ~47.6 bits of entropy and must be protected by throttling, progressive
delays, lockouts, and monitoring. The API runs multiple replicas, so in-process counters alone are
incorrect. Adding Redis is possible but introduces a new operational component.

## Decision

Two layers:

**1. Edge/coarse limits — ASP.NET Core rate limiter (in-process, per replica)**

| Policy | Partition | Limit |
|---|---|---|
| `portal` | user ID | 300 requests/min, burst 60 |
| `integration` | integration ID | 600 requests/min, burst 120 |
| `public-metadata` | client IP | 60 requests/min |
| `public-grant` | client IP | 30 requests/min |
| `admin` | user ID | 120 requests/min |

Responses include `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`; 429 includes
`Retry-After`. Limits are configurable. Replica-local limits are acceptable because the
authoritative abuse control is layer 2 and Caddy/the host spreads traffic; worst case an attacker
gets N× the coarse limit (one API replica initially).

**2. Authoritative abuse control — PostgreSQL**

- `download_key_attempts` records each failed attempt (link_id, ip_hash, attempted_at) and is
  checked atomically before verification.
- Rules: 5 failures per link+IP per 15 min → 15-min lockout; progressive delay 0.5/1/2/4 s after
  each failure; 100 failures per link per hour → soft lock + alert; counters cleaned by a job.
- `daily_usage` tracks per-integration bytes/files per day for quotas (atomic upsert).
- IPs are hashed with a daily pepper for privacy; raw IPs are not retained beyond the abuse
  window (download events may store a truncated hash).

**Implementation notes**

- Trust `X-Forwarded-For` only from Caddy (Docker network); configure `ForwardedHeaders` with the
  known proxy network and a single hop. Never trust client-supplied IPs.
- Locked-out requests return 429 `too_many_attempts` **before** key hashing (cheap rejection).
- Metrics: `link_key_failures_total{reason}`, `link_lockouts_total`, `rate_limited_total{policy}`.
- Redis replaces layer 2 only if measurements show contention; the interface
  (`IAbuseGuard`) isolates that change.

## Consequences

- Correct behavior across replicas without Redis in v1.
- Additional DB writes on failed key attempts (bounded by throttling); indexes keep it cheap.
- Coarse limits may allow a burst per replica; acceptable, documented.
- Lockout state is durable across restarts and shared by all workers/API instances.

## Alternatives considered

- In-memory only: rejected (bypass by hitting different replicas).
- Redis from day one: rejected (extra component for modest traffic; revisit at scale).
- WAF rate rules only: useful as an outer layer, insufficient for per-link/key semantics.
- CAPTCHA: rejected for API/link UX in v1; can be added to the public landing page later.
