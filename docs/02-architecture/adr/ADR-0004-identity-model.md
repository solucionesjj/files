# ADR-0004 — Identity: Entra ID for humans, client credentials + API keys for machines

- **Status:** Accepted (API keys provisional per OQ-11)
- **Date:** 2026-10-23
- **Deciders:** Security, Architecture

## Context

The source requires Microsoft Entra ID for human users, OAuth2 client credentials as the preferred
machine mechanism, and API keys only for compatibility. Human and machine identities must be
isolated, and a key must not become a universal credential. One Entra tenant per deployment is
assumed (OQ-03), with identification by (`tid`, `oid`).

## Decision

**Humans**

- SPA uses OIDC Authorization Code + PKCE with MSAL; the API validates JWT signature, `iss`,
  `aud`, `exp`/`nbf`, and `tid == Entra:TenantId`.
- Identity key is (`tenant_id`, `object_id`) from `tid`/`oid`; email is an attribute, not a key.
- Users are provisioned JIT. Roles come from app roles (`Files.User`, `Files.Admin`,
  `Files.Auditor`) and are re-evaluated per request; the database never stores roles.
- API accepts only delegated user tokens for portal endpoints; app-only tokens are rejected on
  portal/admin routes.

**Machines**

- Primary: Entra client credentials. The integration stores `entra_client_id` (and the service
  principal `object_id` for audit); the token's `azp`/`appid` must match an active integration,
  and its app-role claims must map to that integration's permissions. Tokens from unregistered
  apps are rejected.
- Fallback: API keys, format `flk_<kid>_<secret>`; `kid` = 12 base32 chars (lookup), `secret` =
  32 random bytes base64url. Only `HMAC-SHA256(pepper, secret)` is stored; verification is
  constant-time. The plaintext is returned exactly once. Keys have expiry, revocation, rotation
  overlap, per-integration scope, and optional IP allowlist.
- Key material peppers come from configuration (`Auth__ApiKeyPepper`) and can be rotated with a
  versioned prefix in the stored hash (see runbook).
- No machine credential can cross integration boundaries; authority is always
  integration ID + permission + file ownership/grant.

## Consequences

- Two authentication handlers plus a policy scheme that routes `flk_` to API keys and JWT
  otherwise. Endpoint policies decide which actor types are allowed.
- API key lookup is O(1) by `kid`; a leaked key is revocable without touching the integration.
- Entra app registrations are an operational dependency per integration (admin runbook).
- API key support adds secret-handling, rotation, and scanning obligations; it can be disabled
  globally by feature flag if unused.

## Alternatives considered

- Only API keys: rejected (rotation and audit weaker; spec prefers Entra).
- Only Entra: rejected for legacy compatibility (OQ-11), but it remains the default path.
- Storing API keys encrypted instead of hashed: rejected (reversible secrets are unnecessary and
  riskier).
- Argon2id for API keys: rejected (unnecessary for 256-bit secrets, adds verification cost).
