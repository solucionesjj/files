# Roles, Personas, and Authorization Matrix

Authorization is enforced exclusively by the API. The Portal hides actions the user cannot
perform, but hiding is never the control. Denials return `403 forbidden` or `404 not_found`
depending on whether the existence of the resource itself is sensitive (see
[error-catalog.md](../04-api/error-catalog.md)).

## 1. Human roles (Entra app roles)

| Role | Claim value | Granted to | Purpose |
|---|---|---|---|
| User | `Files.User` | All tenant employees (default assignment) | Manage and share own files. |
| Administrator | `Files.Admin` | Platform administrators | Global policy, integrations (Entra app binding), legal hold, retention changes, deletion. |
| Auditor | `Files.Auditor` | Security/compliance | Read-only audit events, download events, aggregate usage. No content. |

Rules:

- Roles come from the validated JWT `roles` claim (app roles) and are re-evaluated per request.
- `Files.Admin` implies all `Files.User` permissions. `Files.Auditor` does **not** imply user
  file access, but every authenticated tenant member also has `Files.User` unless assigned
  otherwise; an auditor who is also a user sees only their own files.
- No role grants access to another user's file content (D-19). Content access is via ownership,
  explicit link, or integration grant.
- A user with zero recognized roles is denied portal features but may still sign in and see an
  "access not configured" page.

## 2. Integration permissions

| Permission | Allows | Default |
|---|---|---|
| `file.write` | Create upload sessions, upload, complete, read metadata of owned files. | Off |
| `file.read` | Read metadata and content of owned files and files explicitly granted. | Off |
| `file.delete` | Logical + physical deletion of owned files. | Off |

- Permissions are stored in `integration_permissions`; at least one is required to activate.
- `file.delete` does not imply `file.read`; `file.read` does not imply `file.write`.
- Cross-integration grants (`integration_file_grants`) always grant **read-only metadata +
  content** and never delete/write.
- Disabling an integration revokes effective access immediately, regardless of credential state.

## 3. Portal permission matrix

Legend: ✅ allowed · 🔸 allowed with conditions · ❌ denied.

| Capability | User (owner) | Admin | Auditor |
|---|---|---|---|
| Sign in, view own profile | ✅ | ✅ | ✅ |
| Upload file | ✅ | ✅ | ❌ |
| List/search own files | ✅ | ✅ | ❌ |
| View own file metadata | ✅ | ✅ | ❌ |
| Download own file content | ✅ | ❌ (D-19) | ❌ |
| Rename / tag own file | ✅ | ✅ | ❌ |
| Change own file retention (within policy) | 🔸 if `allow_user_override` | ✅ | ❌ |
| Delete own file | ✅ | ✅ | ❌ |
| Create/revoke own links | 🔸 public only if policy allows | ✅ | ❌ |
| View own download stats | ✅ | ✅ | ❌ |
| List all users' file metadata | ❌ | ✅ | ❌ |
| Download another user's content | ❌ | ❌ | ❌ |
| Override retention / bulk migration | ❌ | ✅ (reason required) | ❌ |
| Delete any file | ❌ | ✅ (reason required) | ❌ |
| Manage integrations/permissions | ❌ | ✅ | ❌ |
| Bind/change an integration's Entra app ID | ❌ | ✅ | ❌ |
| Place/release legal hold | ❌ | ✅ | ❌ |
| Manage retention/notification policy | ❌ | ✅ | ❌ |
| View audit events | ❌ (own file events only, limited) | ✅ | ✅ |
| View aggregate usage/cost | ❌ | ✅ | ✅ |
| Direct database / S3 access | ❌ | ❌ | ❌ |

Conditional rules:

- Owner access is always required for metadata changes; admins change retention/deletion but
  not content.
- Public link creation requires `platform_settings.public_links_enabled = true` and a
  per-file enabled policy. Private links are always available to owners.
- Auditors can see audit records that contain file IDs and metadata (filenames) but never
  object keys, tokens, keys, or content.

## 4. Link authorization model

### Public link

1. Token resolves to an `active` link with `access_mode = public`.
2. File state is downloadable (see invariant 4 in requirements).
3. Link `expires_at` is null or in the future; `successful + reserved < max_downloads` when set.
4. Supplied key verifies against the Argon2id verifier.
5. Abuse controls (per IP/link) allow the attempt.
6. A grant is issued (120 s, single use) and the download is streamed; completion increments the
   count.

No Entra token is involved. The visitor needs the link **and** the key; neither alone suffices.

### Private link

1. Token resolves to an `active` link with `access_mode = private`.
2. Caller has a valid Entra token from the configured tenant (`tid` match). Before sign-in, the
   public metadata endpoint reveals only that authentication is required; filename/size are shown
   after authentication via `GET /v1/download-links/{token}`.
3. User is provisioned/active in `users`.
4. File state is downloadable; link limits/expiry are valid.
5. Optional future restriction (`allowed_object_ids`, not in v1) would be evaluated here.
6. A grant is issued, attributed to the user for audit; download completion is audited with the
   user ID.

Tenant membership alone is never sufficient: the link must be active, the file available, and
(for a restricted private link) the user must be in the ACL or a member of an allowed group.

### Grant redemption

Grants are bearer capability tokens (the link/job is the capability), 256-bit random, unguessable,
hashed at rest, single-use, valid for 120 s, and bound to the link, file, and (for private links)
the issuing user for audit. Redemption does not require the Entra token because the browser must
be able to navigate to the download URL without an `Authorization` header; the short TTL and
single use limit exposure. A grant never contains S3 credentials, and the response sets
`Cache-Control: no-store` and `Referrer-Policy: no-referrer`.

## 5. Ownership and access resolution (pseudo-code)

```
authorize_content(actor, file):
    if actor.type == INTEGRATION:
        require file.owner_integration_id == actor.id, or
                exists grant(file.id, actor.id)
        require permission file.read
        require integration.active
    else:
        if file.owner_user_id == actor.user_id: allow
        elif request has a valid link grant for file: allow per link rules
        else: deny 404
    require file.status == available
    require file.expires_at is null or file.expires_at > now()
    require file.scan_status in (clean, not_scanned)
```

Search and listing apply the same predicate in SQL, never post-filtering in memory.

## 6. Administrative boundaries

- Admins manage integrations across the deployment but cannot read their file content.
- Admins define the public/private link TTLs; owners cannot override them.
- Admin actions require a `reason` (≥10 chars) for: retention change, bulk migration, legal hold,
  file deletion, and integration disable.
- Admin actions are audited with actor, reason, before/after, and trace ID.
- There is no "superuser" bypass of S3 encryption or database encryption.

## 7. Break-glass (future)

Support content access may be added later as an explicit, time-boxed, dual-approved flow with
full audit and customer notification. It is **not** implemented in v1. If OQ-05 is answered
"yes", add a new story and threat-model entry before implementation.
