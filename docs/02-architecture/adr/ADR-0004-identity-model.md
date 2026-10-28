# ADR-0004 — Identity: Entra ID for humans and machines (no API keys)

- **Status:** Superseded by ADR-0014 (2026-10-27: Entra authenticates humans only; roles and API
  keys live in Files; private links deferred)
- **Date:** 2026-10-23
- **Deciders:** Security, Architecture

> **Superseded.** Kept for history. The owner later narrowed the scope: Entra ID is used only to
> authenticate tenant humans; user roles are stored in Files; integrations use Files-issued API
> keys; private links and Graph directory access are out of scope for v1. See
> [ADR-0014](ADR-0014-entra-human-auth-only.md).

## Context

The source requires Microsoft Entra ID for humans and OAuth 2.0 client credentials as the preferred
machine mechanism. It allowed API keys only for compatibility. The owner confirmed **one Entra
tenant per deployment** and **Entra-only machine authentication**, so Files does not need a second
credential lifecycle or store any client secret.

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

- Entra client credentials only. The integration stores `entra_client_id` (and the service
  principal `object_id` for audit); the token's `azp`/`appid` must match an active integration.
- The `Files.Integration` role proves the app was granted the API permission; the **actual
  permissions are read from Files** (`file.write`, `file.read`, `file.delete`), never from token
  scopes.
- **No API keys, no client secrets, and no shared symmetric credentials are stored by Files.**
  The consumer owns its Entra secret/certificate; rotation happens in Entra. Files needs no
  notification when the consumer rotates.
- Unregistered, disabled, or IP-restricted integrations are rejected; authority is always
  integration ID + permission + file ownership/grant.

**Directory data for link ACLs**

- Private links can target specific Entra users/groups. The API needs read-only directory access
  to search users/groups and evaluate membership:
  - Graph application permissions `User.ReadBasic.All`, `Group.ReadBasic.All`, and
    `GroupMember.Read.All` (admin consent), scoped to the tenant;
  - or the token's `groups` claim for group checks, with Graph fallback when the claim is absent
    or over the limit.
- The directory search endpoint is portal-only, rate-limited, and returns only object ID, display
  name, and (for users) email; no other directory data is exposed.

## Consequences

- One authentication handler for humans and one for app tokens; no API key handler, pepper, or
  credential table.
- A compromised consumer secret is handled in Entra (disable the app/rotate); Files rejects the
  app as soon as its integration is disabled or its client ID is removed.
- The directory integration adds Graph app permissions and a cache; if Graph is unavailable, new
  link ACL editing fails closed while existing links keep working.
- Entra application registration remains an operational dependency per integration.

## Alternatives considered

- **API keys** (previous design): rejected by OQ-11; removes rotation, hashing, and leak surface.
- **Storing client secrets in Files:** rejected (no need; Entra validates the app).
- **Group membership only from the token `groups` claim:** insufficient for large tenants
  (overage) and for dynamic groups; Graph fallback is required.
- **No private-link ACL:** rejected by OQ-04; specific users/groups or all-tenant are required.
