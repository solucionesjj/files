# API Conventions

**Contract:** [`openapi/files-api.v1.yaml`](openapi/files-api.v1.yaml) (OpenAPI 3.1)
**Base URL:** `https://files.<domain>/v1` · **Errors:** [`error-catalog.md`](error-catalog.md)

## 1. Versioning and compatibility

- Version is in the path (`/v1`). There is no header-based versioning.
- Non-breaking (allowed in `/v1`): new endpoints, new optional request fields, new response fields,
  new error codes, new enum values (clients must tolerate unknown values).
- Breaking (requires `/v2`): removing/renaming fields, changing types/semantics, making optional
  fields required, tightening validation, changing auth.
- Deprecation: responses include `Deprecation: true` and `Sunset: <HTTP-date>`; changelog and 90-day
  notice minimum.

## 2. Authentication and authorization

| Caller | Scheme | Header | Token |
|---|---|---|---|
| Portal user | Entra delegated JWT | `Authorization: Bearer <jwt>` | Audience `api://<api-client-id>`, scope `Files.Access`, `tid` = configured tenant. |
| Integration (preferred) | Entra client credentials | `Authorization: Bearer <jwt>` | App token whose `azp`/`appid` matches the integration and `roles` include the required permission. |
| Integration (fallback) | API key | `Authorization: Bearer flk_<kid>_<secret>` | Scoped to one integration; 256-bit secret. |
| Public link visitor | — | — | Link token in path + download key in body. |
| Grant redemption | — | — | 256-bit grant token in path (capability, 120 s, single use). |

- No cookies, no query-string credentials. Credentials in URLs are rejected.
- `GET /v1/me` returns the caller identity, roles, and effective permissions.
- 401 means unauthenticated/invalid credential; 403 means authenticated but not permitted.
  Resource existence is hidden with 404 where revealing it would help an attacker.

## 3. Requests and responses

- JSON UTF-8; field names `camelCase`; unknown request fields are rejected (`400`).
- Binary responses: `application/octet-stream`; uploads to S3 via presigned URLs.
- IDs are UUID strings. Timestamps are RFC 3339 UTC (`2026-10-23T21:00:00Z`). Sizes are bytes.
- Every response includes `X-Request-Id` (propagated/generated) and `traceId` in errors.
- `Cache-Control: no-store` on all authenticated and link responses.
- `ETag` on file metadata; `If-Match` is required for `PATCH /portal/files/{id}` (412 on mismatch).

## 4. Errors (RFC 9457)

```json
{
  "type": "https://files.example.com/problems/file-unavailable",
  "title": "File unavailable",
  "status": 404,
  "code": "file_unavailable",
  "detail": "The file is not available for download.",
  "instance": "/v1/integrations/files/0199...",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "retryable": false
}
```

Validation errors add `errors` as a field → message-array map:

```json
{ "status": 400, "code": "validation_error", "errors": { "sizeBytes": ["must be greater than 0"] } }
```

Security-sensitive operations (links, grants, foreign integrations) return the **same** 404 body
for invalid, revoked, expired, unauthorized, or missing resources. Detailed reasons are recorded
only in audit events.

## 5. Pagination, filtering, sorting

- Cursor pagination: `limit` 1–100 (default 25), `cursor` opaque; response
  `{ "items": [...], "nextCursor": "...", "hasMore": true }`.
- Invalid/expired cursors return `400 invalid_cursor` (do not silently restart).
- Sorting: `sort=field` / `sort=-field`; supported: `created_at`, `name`, `downloads`.
- Filters: `q` (filename, case-insensitive partial), `tag` (repeatable, AND semantics),
  `status` (repeatable), `expiresBefore`/`expiresAfter` (RFC 3339), `expiringWithinDays`.
- Unknown sort/filter fields → `400 invalid_query_parameter`.
- Total counts are not returned on large lists (cost); list endpoints expose `hasMore`.

## 6. Idempotency

- Header `Idempotency-Key: <8–128 printable chars>` on mutating integration requests
  (`POST /integrations/upload-sessions` and `POST /integrations/files`). Link creation is not
  idempotent because the response contains a secret (token/key) that must not be persisted for
  replay.
- Scoped to the authenticated integration/user. Retained 24 h.
- Same key + same request hash → original response replayed (same status/body).
- Same key + different body → `422 idempotency_key_reuse`.
- Concurrent duplicate while in progress → `409 idempotency_in_progress` with `Retry-After: 1`.
- Missing header is allowed (operation is still idempotent by state machine where possible) but
  strongly recommended for automated consumers.

## 7. Upload protocol

### 7.1 Direct multipart (default, up to 5 GiB)

1. `POST /portal/upload-sessions` or `POST /integrations/upload-sessions` with size, content
   type, optional SHA-256, optional tags/retention (portal only).
2. Response: `fileId`, `uploadSessionId`, `mode` (`multipart`/`single`), `expiresAt`,
   `partSizeBytes`, `partCount`, and presigned parts (first batch or all).
3. For parts not returned, call
   `POST /portal/upload-sessions/{id}/parts` (or `/integrations/upload-sessions/{id}/parts`) with part numbers.
4. `PUT` each part to S3 with the signed headers. Parts can be uploaded in parallel (client
   concurrency ≤4 recommended); retry a failed part with the same URL before it expires.
5. `POST .../complete`. The server lists parts, completes the S3 upload, verifies size and optional
   checksum, and returns the file (status `available`, or `processing` if scanning is enabled).
6. If the client abandons the upload: `DELETE .../{id}` (abort) or let it expire (60 min) and be
   cleaned (24 h).

Notes:
- Do not send the `Authorization` header to S3; the presigned URL is the credential.
- Do not change `Content-Type` or other signed headers.
- Single mode (`< 64 MiB`): the response has `presignedUrl` and `headers`; `PUT` the body once.
- A presigned URL is bound to one part number and one upload; reuse after expiry fails at S3.

### 7.2 Proxy upload (integration only, ≤100 MiB)

`POST /integrations/files` with `multipart/form-data` (`file`, optional `filename`). Files are
staged in S3 (`staging/`), promoted to the final key, and verified before the response. Use this
only when the consumer cannot reach S3 directly.

## 8. Download protocol

### 8.1 Owner / private link / public link (browser-safe)

1. Obtain a grant:
   - Owner: `POST /v1/portal/files/{fileId}/downloads` (Bearer).
   - Private link: `POST /v1/download-links/{token}/downloads` (Bearer).
   - Public link: `POST /v1/public/download-links/{token}/downloads` with `{ "key": "Ab3xY9Qz" }`.
2. Response `201`: `{ "grantToken": "...", "expiresAt": "...", "downloadUrl": "/v1/download-grants/..." }`.
3. `GET /v1/download-grants/{grantToken}` streams the file (no auth header; browser navigation).
4. The grant is single-use and expires in 120 s. A second use returns `404 grant_not_found`.
5. The successful download count increments only when the stream completes.

### 8.2 Integration

`GET /v1/integrations/files/{fileId}/content` (Bearer, permission `file.read`). Supports
`Range`. Each completed response increments the count. Denials are audited.

### 8.3 Counting rule

- A download counts once when the response body is fully written to the client.
- Reservations count against `maxDownloads` while pending and are released on failure/expiry.
- Range requests on the integration content endpoint count once per completed response.
- Grant downloads are one stream per grant, so one count per grant.

## 9. Rate limits

| Policy | Applies | Default |
|---|---|---|
| `portal` | User | 300 req/min |
| `integration` | Integration | 600 req/min |
| `admin` | Admin user | 120 req/min |
| `public-metadata` | IP | 60 req/min |
| `public-grant` | IP | 30 req/min |
| `key-attempts` | Link + IP | 5 failures / 15 min, then lockout |

Headers: `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`; `429` includes `Retry-After`.
Key-attempt lockouts return generic `404 link_not_found` for invalid attempts and `429
too_many_attempts` once locked (documented compromise: the lockout itself is not a secret).

## 10. HTTP semantics and status codes

| Code | Used for |
|---|---|
| 200 | Read/update/complete success, stream. |
| 201 | Upload session, link, grant, integration, credential created. |
| 202 | Asynchronous admin action (retention migration, deletion request). |
| 204 | Abort/revoke/delete with no body. |
| 206 | Partial content (Range). |
| 400 | Malformed JSON, unknown fields, invalid query. |
| 401 | Missing/invalid/expired credential. |
| 403 | Authenticated but not permitted (token lacking permission, disabled feature, IP). |
| 404 | Not found or intentionally hidden. |
| 409 | Conflict/state (idempotency in progress, upload already completed). |
| 410 | Non-recoverable expiry (upload session or grant). |
| 412 | `If-Match` mismatch. |
| 413 | Payload too large (proxy upload, JSON body). |
| 415 | Unsupported media type. |
| 422 | Semantic validation (content type, retention, size mismatch, idempotency reuse). |
| 429 | Rate/quota exceeded. |
| 500 | Unhandled error (trace ID only). |
| 507 | Deployment storage quota exhausted. |
| 503 | Dependency unavailable (DB/S3) or maintenance. |

## 11. CORS and security headers

- CORS allows only configured portal origins; methods `GET, POST, PATCH, DELETE, OPTIONS`;
  headers `Authorization, Content-Type, Idempotency-Key, If-Match, X-Request-Id, Range`;
  exposed `X-Request-Id, RateLimit-*, Retry-After, ETag`; no credentials.
- Responses add `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  `Strict-Transport-Security` (at the edge), and `Content-Security-Policy` for the SPA host.
- `Content-Disposition` uses RFC 5987 (`filename*=UTF-8''...`) with an ASCII fallback and strips
  path separators/control characters.

## 12. Endpoint summary

| Method | Path | Actor | Purpose |
|---|---|---|---|
| GET | `/v1/config` | public | Portal bootstrap config. |
| GET | `/v1/me` | user | Effective identity/permissions. |
| GET | `/v1/portal/files` | user | Search/list own files. |
| POST | `/v1/portal/upload-sessions` | user | Create upload session. |
| POST | `/v1/portal/upload-sessions/{id}/parts` | user | Presign more parts. |
| POST | `/v1/portal/upload-sessions/{id}/complete` | user | Complete upload. |
| DELETE | `/v1/portal/upload-sessions/{id}` | user | Abort upload. |
| GET | `/v1/portal/files/{fileId}` | user | File metadata. |
| PATCH | `/v1/portal/files/{fileId}` | user | Rename/tags/retention. |
| DELETE | `/v1/portal/files/{fileId}` | user | Delete file. |
| POST | `/v1/portal/files/{fileId}/downloads` | user | Issue owner download grant. |
| GET | `/v1/portal/files/{fileId}/links` | user | List links for a file. |
| POST | `/v1/portal/files/{fileId}/links` | user | Create link (+key). |
| GET | `/v1/portal/links` | user | List my links. |
| DELETE | `/v1/portal/links/{linkId}` | user | Revoke link. |
| GET | `/v1/portal/links/{linkId}/stats` | user | Link statistics. |
| GET | `/v1/portal/tags` | user | Tag suggestions. |
| GET | `/v1/public/download-links/{token}` | public | Link landing metadata (private links expose only that auth is required). |
| GET | `/v1/download-links/{token}` | user | Full metadata for a private link after sign-in. |
| POST | `/v1/public/download-links/{token}/downloads` | public | Verify key, issue grant. |
| POST | `/v1/download-links/{token}/downloads` | user | Private link grant. |
| GET | `/v1/download-grants/{grantToken}` | public | Stream file via grant. |
| POST | `/v1/integrations/upload-sessions` | integration | Create upload session. |
| POST | `/v1/integrations/upload-sessions/{id}/parts` | integration | Presign more parts. |
| POST | `/v1/integrations/upload-sessions/{id}/complete` | integration | Complete upload. |
| DELETE | `/v1/integrations/upload-sessions/{id}` | integration | Abort upload. |
| POST | `/v1/integrations/files` | integration | Proxy upload ≤100 MiB. |
| GET | `/v1/integrations/files` | integration | List owned files. |
| GET | `/v1/integrations/files/{fileId}` | integration | Metadata. |
| GET | `/v1/integrations/files/{fileId}/availability` | integration | Availability check. |
| GET | `/v1/integrations/files/{fileId}/content` | integration | Download content. |
| DELETE | `/v1/integrations/files/{fileId}` | integration | Delete owned file. |
| GET | `/v1/admin/integrations` | admin | List integrations. |
| POST | `/v1/admin/integrations` | admin | Create integration. |
| GET | `/v1/admin/integrations/{id}` | admin | Integration detail. |
| PATCH | `/v1/admin/integrations/{id}` | admin | Update/disable. |
| PUT | `/v1/admin/integrations/{id}/permissions` | admin | Replace permissions. |
| GET | `/v1/admin/integrations/{id}/credentials` | admin | List credentials. |
| POST | `/v1/admin/integrations/{id}/credentials` | admin | Issue credential/key. |
| DELETE | `/v1/admin/credentials/{credentialId}` | admin | Revoke credential. |
| GET | `/v1/admin/files` | admin | All file metadata. |
| POST | `/v1/admin/files/{fileId}/retention` | admin | Override retention. |
| POST | `/v1/admin/files/{fileId}/deletion` | admin | Request deletion. |
| GET | `/v1/admin/files/{fileId}/grants` | admin | List integration grants for a file. |
| POST | `/v1/admin/files/{fileId}/grants` | admin | Grant integration read access. |
| DELETE | `/v1/admin/grants/{grantId}` | admin | Revoke integration grant. |
| GET | `/v1/admin/retention-policy` | admin | Current + history. |
| POST | `/v1/admin/retention-policy` | admin | New policy version. |
| POST | `/v1/admin/retention-migrations` | admin | Bulk migration. |
| GET | `/v1/admin/retention-migrations/{id}` | admin | Migration status. |
| GET | `/v1/admin/notification-policy` | admin | Current notification policy. |
| PUT | `/v1/admin/notification-policy` | admin | New notification policy version. |
| GET | `/v1/admin/email-templates` | admin | List templates. |
| PUT | `/v1/admin/email-templates/{key}/{locale}` | admin | New template version. |
| GET | `/v1/admin/audit-events` | admin/auditor | File/admin audit events. |
| GET | `/v1/admin/download-events` | admin/auditor | Download attempts. |
| GET | `/v1/admin/usage` | admin/auditor | Storage/download usage. |
| GET | `/v1/admin/settings` | admin | Platform settings. |
| PATCH | `/v1/admin/settings` | admin | Update platform settings. |
| GET | `/health/live` | internal | Liveness. |
| GET | `/health/ready` | internal | Readiness (DB/S3/migrations). |

## 13. Examples

### Integration upload (cURL)

```bash
TOKEN=$(curl -s -X POST "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d "grant_type=client_credentials&client_id=$CLIENT_ID&client_secret=$SECRET&scope=api://$API_ID/.default" \
  | jq -r .access_token)

curl -s -X POST https://files.example.com/v1/integrations/upload-sessions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{"fileName":"customer-4711.pdf","sizeBytes":10485760,"contentType":"application/pdf",
       "checksumSha256":"<hex>"}'
```

The response includes presigned part URLs; upload each with `curl --upload-file part.bin <url>`
(no Authorization header), then call `/complete`.

### Public download (cURL)

```bash
curl -s -X POST https://files.example.com/v1/public/download-links/$TOKEN/downloads \
  -H "Content-Type: application/json" -d '{"key":"Ab3xY9Qz"}'
# -> {"grantToken":"...","expiresAt":"...","downloadUrl":"/v1/download-grants/..."}
curl -L -o file.pdf "https://files.example.com${DOWNLOAD_URL}"
```
