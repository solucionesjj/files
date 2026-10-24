# ADR-0005 — Download links: 256-bit tokens, 8-character keys, 120-second grants

- **Status:** Accepted
- **Date:** 2026-10-23
- **Deciders:** Security, Architecture, Product

## Context

The source mandates public links with an associated 8-character alphanumeric, case-sensitive key,
never embedded in the URL, stored as a verifier, and verified before serving content. Because the
key has limited entropy, throttling and monitoring are required. Download counts must not increase
merely because a URL was generated, and concurrent requests must have defined semantics.

## Decision

**Link token**

- 32 random bytes (256 bits) from a CSPRNG, base64url without padding (43 chars), in the path:
  `/d/{token}`. Only `SHA-256(token)` is stored for lookup (`download_links.token_hash`, unique).
- Token alone is not sufficient for public links.

**Lifetime and private-link ACL**

- New links always receive `expires_at = now() + admin TTL`: **public 15 days**, **private 30
  days** by default, configurable in `platform_settings`. The owner cannot change the lifetime;
  existing links keep their expiry.
- Public links have no ACL and always require the key.
- Private links store either `allow_all_tenant = true` **or** ACL rows
  (`download_link_acl`: Entra user/group object IDs). Redemption requires an active tenant user
  who is in the ACL, or any active tenant user when `allow_all_tenant` is true. Group membership
  is checked from the `groups` claim when present, with Graph fallback (cached).
- Private links never have a download key; the key is a public-link second factor.

**Download key**

- Exactly 8 chars from `[A-Za-z0-9]` (62^8 ≈ 2.18×10^14 ≈ 47.6 bits), CSPRNG, case-sensitive.
- Stored as `Argon2id(pepper || key)` with a per-key random salt, parameters m=19 MiB, t=2, p=1
  (OWASP baseline). `download_key_version` records the pepper version for rotation.
  Verification is constant-time inside the library.
- Never in the URL, never logged, never returned after creation; a new link (new token + key) is
  required to rotate.
- Throttling: 5 failed attempts per link+IP per 15 min, progressive delay (0.5/1/2/4 s), 15-min
  lockout, and 100 failures per link per hour triggers a soft lock + alert. Counters are
  Postgres-backed so all replicas share them.
- All failures return the same 404 `link_not_found` body and comparable latency (verification is
  attempted when not locked out; a dummy hash verify is used for unknown tokens to avoid timing
  differences).

**Grants and counting**

- A successful key + link + file validation creates a `download_grants` row: 256-bit token,
  SHA-256 at rest, 120 s TTL, single use, bound to link/file (and user when authenticated).
- If the link has `max_downloads`, issuance atomically reserves a slot
  (`successful + reserved < max`); reservation is released on expiry/failure and converted on
  success.
- The grant is redeemed by `GET /v1/download-grants/{token}` (capability URL; no bearer header,
  so the browser can navigate). The response sets `no-store` and `no-referrer`.
- `files.successful_download_count` and `download_links.successful_downloads` increment only when
  the response stream completes. A crash between streaming and completion may lose one count;
  this is accepted and documented (metrics expose the gap).
- Range requests are not supported on grant downloads (single stream, single count). The
  integration content endpoint supports `Range`; each completed response counts once.

## Consequences

- Link lifetimes are predictable and bounded; owners who need another period create a new link.
- ACL checks add a directory dependency (Graph) for group membership; failures fail closed for
  new edits while existing user-ID ACLs keep working.

- 47.6 bits is only safe with throttling; the key is a second factor, not the primary secret.
- Argon2id verification costs ~19 MiB and a few ms per attempt; the attempt throttle bounds
  concurrency, and a lockout path skips hashing.
- Grants add one round trip per download but enable browser-safe streaming, precise counting, and
  immediate revocation (grant + link status checked at redemption).
- A database outage blocks downloads (fail closed).

## Alternatives considered

- 12-character key: rejected — spec says exactly 8.
- Embedding the key in the URL fragment: rejected — spec explicitly forbids embedding, and
  fragments still leak via history/logs in some clients.
- Storing keys in plaintext/reversible encryption: rejected.
- Presigned S3 GET after key verification: rejected (no completion counting or revocation; see
  ADR-0003).
