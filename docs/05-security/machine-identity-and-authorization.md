# Machine Identity and Authorization

**Decisions:** ADR-0014, ADR-0011 · **Requirements:** FR-M2M-001..014, FR-INT-001..011

Machine authentication uses **Files-issued API keys**. Files does not use Entra client credentials,
does not store client secrets, and does not accept shared symmetric credentials other than its own
hashed keys.

## 1. Registration model

An integration is created in one step by an administrator:

1. `POST /v1/admin/integrations` with `slug`, `name`, permissions, and an optional IP allowlist.
2. Then `POST /v1/admin/integrations/{id}/api-keys` issues a key; the secret is returned exactly
   once and never again.

The integration row stores:

- `status` (`active`/`disabled`); disabling is immediate.
- Permissions (`file.write`, `file.read`, `file.delete`), admin-managed and audited.
- `allowed_ip_cidrs` — optional; empty means any source (a valid key is still required).
- Disable/enable metadata with reason and actor.

Each API key (`integration_api_keys`) stores:

- `name`, `prefix` (for lookup), `secret_hash` (SHA-256 of the full key), `scopes`, optional
  `expires_at`, `last_used_at`, and revocation fields. The secret itself is never stored or logged.

## 2. API-key flow

```mermaid
sequenceDiagram
  participant C as Consumer backend
  participant A as Files API
  participant D as PostgreSQL

  C->>A: POST /v1/integrations/upload-sessions (X-Api-Key: fsk_...secret)
  A->>D: find key by prefix; verify hash (constant time)
  A->>D: check integration active, key not revoked/expired, IP allowlist
  A->>D: effective scopes = key scopes ∩ integration permissions
  A->>D: create session/file metadata
  A-->>C: 201 UploadSession
```

Rules:

- The key is looked up by its public prefix and verified in constant time against the SHA-256 hash.
- A key whose integration is disabled, or that is revoked/expired, is rejected
  (`401 invalid_api_key`).
- The `scopes` on a key are a subset of the integration's permissions; the effective capability is
  the intersection of the two.
- No presigned URLs, basic-auth credentials, or Entra client-credentials tokens are accepted.

## 3. Authorization and file access

| Operation | Permission | Ownership/grant |
|---|---|---|
| Create upload session | `file.write` | New file owned by caller. |
| Read metadata | `file.read` | Owned or active `integration_file_grants` row. |
| Check availability | `file.read` | Same. |
| Download content | `file.read` | Same. |
| Delete file | `file.delete` | **Owner only**; grants are read-only. |

- Grants are reserved for v1.1: the `integration_file_grants` table and the read-time grant check are
  in place (a grant, if present, authorizes read-only access), but **no admin endpoint creates or
  revokes them yet** — the S13/M7 owner decision deferred `POST /v1/admin/files/{fileId}/grants`,
  `GET /v1/admin/files/{fileId}/grants`, and `DELETE /v1/admin/grants/{grantId}` out of the contract
  (see the [session log](../09-delivery/session-log.md)). Until then, integration file access is
  owner-scoped only.
- Disabled integrations, expired grants, and deleted/expired files fail closed.
- Cross-integration attempts are audited as denied download events.
- Foreign/unauthorized files return `404 file_not_found` (no existence oracle).

## 4. Per-integration quotas and limits

| Limit | Default | Config |
|---|---|---|
| Max file size | 5 GiB (or lower per integration) | `Storage__MaxFileSizeBytes` + override |
| Concurrent upload sessions | 3 | `platform_settings` / integration |
| Daily ingress bytes | 50 GiB | `daily_usage` |
| Requests per minute | 600 | rate limiter |
| API keys per integration | configurable | `platform_settings` |

Quota and staging-capacity checks happen **before** creating a session or accepting any chunk,
and are enforced atomically with `daily_usage` upserts. `429 quota_exceeded` includes
`Retry-After`.

## 5. IP allowlist

- Stored as `cidr[]`; empty = any.
- The API trusts `X-Forwarded-For` only from Caddy and takes the first untrusted hop; direct
  connections to the API port are impossible from the internet.
- IPv4 and IPv6 are supported. `403 ip_not_allowed` is logged with the resolved IP (hashed in
  audit).
- Changing the allowlist is audited and takes effect immediately.

## 6. Audit events

| Event | Data |
|---|---|
| Integration created/updated/disabled | Admin actor, reason, before/after. |
| API key created/revoked | Admin actor, key id + prefix (never the secret), reason. |
| Upload session created/completed | Integration, file ID, size, IP hash. |
| Metadata/content access | Integration, file ID, result, IP hash, trace ID. |
| Access denied | Reason code (`no_grant`, `disabled`, `ip_not_allowed`, `unavailable`). |
| File deleted | Integration actor, prior state, result. |

## 7. Key rotation and revocation

1. Admin issues a new key (`POST .../api-keys`), copies the secret once, and hands it to the
   consumer out of band.
2. The consumer deploys the new key; calls succeed.
3. Admin revokes the old key (`DELETE .../api-keys/{keyId}` with a reason); it stops working
   immediately.

If a key leaks: revoke it in Files immediately (blocks that key), review `download_events`/
`file_audit_events`, and issue a replacement. Disabling the integration blocks all of its keys at
once.

## 8. Error behavior

| Situation | Status/code |
|---|---|
| Missing header | 401 `invalid_api_key` |
| Unknown/invalid key | 401 `invalid_api_key` |
| Revoked or expired key | 401 `invalid_api_key` |
| Disabled integration | 401 `invalid_api_key` |
| Valid key, missing scope/permission | 403 `insufficient_permission` |
| IP outside allowlist | 403 `ip_not_allowed` |
| File not owned/no grant | 404 `file_not_found` |
| File expired/quarantined | 404 `file_unavailable` |
| Quota exceeded | 429 `quota_exceeded` |

## 9. Consumer obligations

- Store the API key in a secret manager; never in source, a browser, or logs.
- Send it only from the backend, in the `X-Api-Key` header, over HTTPS.
- Send an `Idempotency-Key` on create operations and retry with the same key.
- Treat 404 as "not accessible"; never retry 4xx except 429.
- Validate the user's business authorization **before** requesting a file; Files enforces only
  file-level access.
- Persist the returned `fileId` with the business record; do not store file bytes.
