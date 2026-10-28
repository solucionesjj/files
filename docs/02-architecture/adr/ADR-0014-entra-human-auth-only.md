# ADR-0014 — Entra ID authenticates humans only; Files owns roles and API keys

- **Status:** Accepted (supersedes ADR-0004; amends ADR-0005)
- **Date:** 2026-10-27
- **Deciders:** Product, Security, Architecture

## Context

The owner confirmed a narrower identity model than ADR-0004 assumed:

- Microsoft Entra ID exists **only to authenticate tenant employees** (their accounts and passwords
  live in Entra). It is not used for machine identity, directory lookups, or application roles.
- **Other systems (integrations) authenticate with API keys** managed by Files, not with Entra
  client credentials.
- **Private download links are out of scope for v1.** Only public links (8-character key) are
  offered, so no Microsoft Graph directory access is needed.
- **User roles live in Files**, not in Entra app roles: Entra answers "who is this user"; Files
  answers "what may this user do".

This removes the Graph dependency, the per-integration Entra registrations, and the Entra
app-role administration, at the cost of Files owning a credential lifecycle and a role store.

## Decision

**Entra ID (humans only)**

- One Entra app registration is enough: it acts as the SPA (public client, PKCE) **and** exposes
  the API scope `Files.Access`. Two registrations (client + resource) remain supported and
  preferred when the portal and API need independent lifecycles.
- The API validates JWT signature, `iss`, `aud`, `exp`/`nbf`, and `tid == Entra:TenantId`, and
  requires the delegated scope `Files.Access`. The scope is still needed so the SPA receives an
  **access token** for the API; ID tokens are never accepted as API credentials.
- **No Entra app roles.** The token's `roles` claim is ignored for authorization. JIT provisioning
  creates the user with role `user`.
- **No Entra client credentials for machines**, and no Graph permissions.

**Authorization (Files)**

- `users.role` is `user`, `admin`, or `auditor`; admins change it in the portal, and every change is
  audited. Admins inherit user capabilities; the auditor role is independent.
- The first admin is bootstrapped by SQL during deployment (documented in the runbook); there is no
  self-promotion path.
- The API re-reads the role from Files on every request, so a revoked/demoted user loses access on
  the next request (a token alone no longer grants a role).

**Machine authentication (API keys)**

- Integrations authenticate with **API keys issued and stored by Files**:
  - 256-bit random secret, shown **once** at creation; only a hash is stored.
  - A short public `prefix` enables lookup; the secret is verified in constant time.
  - Keys carry scopes (`file.write`, `file.read`, `file.delete`), an optional expiry, a revocation
    timestamp, and `last_used_at`.
  - Keys are listed by prefix/name only; they can be rotated (create new, revoke old) and revoked
    without touching other credentials.
  - IP allowlists remain supported per integration.
- `integrations.entra_client_id` is deprecated; new integrations do not need an Entra registration.
- The legacy `BearerIntegration` (Entra client-credentials) path is kept only until the API-key
  handler replaces it and then removed.

**Private links**

- Not offered in v1. Only **public links** with the 8-character key (ADR-0005) are implemented.
- `download_link_acl` and `allow_all_tenant` remain in the schema for a future release but are
  unused; no Graph integration, no directory search endpoint.

## Consequences

- **Simpler Entra footprint:** one registration, no app roles, no Graph consent, no per-integration
  app registrations.
- **Files owns authorization and credentials:** role store (`users.role`) and API-key lifecycle
  (issue, list, rotate, revoke, audit, rate-limit). The threat model gains key-leakage and
  brute-force cases.
- **Schema:** migration `V3` adds `users.role` and `integration_api_keys`; `integrations.entra_client_id`
  becomes optional/deprecated.
- **Contract:** OpenAPI loses the private-link ACL/directory endpoints and gains API-key
  administration; integration authentication changes from bearer JWT to API key.
- **Operations:** the first admin must be seeded; API keys must be rotated on a schedule; a leaked
  key is revoked immediately.
- **Trade-off:** API keys are long-lived shared secrets, simpler for consumers but weaker than
  short-lived tokens. Mitigated by hashing, scoping, expiry, revocation, IP allowlists, audit, and
  rate limits.

## Alternatives considered

- **Keep Entra client credentials for machines** (ADR-0004): rejected by the owner; it requires an
  Entra registration and admin consent per integration.
- **Use the ID token as the API credential:** rejected; ID tokens are not access tokens, carry no
  scope, and must not be sent to APIs.
- **Keep private-link ACLs with Graph:** rejected for v1; adds Graph consent and directory data the
  owner does not want.
- **Store API keys in plaintext:** rejected; hashes only, prefix for lookup.
