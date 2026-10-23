# Upload and Download Design

**Decisions:** ADR-0003, ADR-0005, ADR-0011 · **API:** `docs/04-api/openapi/files-api.v1.yaml`

## 1. Objectives

- Support up to 5 GiB (configurable) without proxying uploads through the API.
- Never expose S3 credentials, bucket names, or object keys to clients.
- Verify every object before it becomes available.
- Count only completed downloads; enforce link limits under concurrency.
- Stream downloads with backpressure, audit every attempt, and revoke immediately.

## 2. S3 layout and bucket policy

| Prefix | Purpose | Lifecycle |
|---|---|---|
| `f/{fileId}` | Final objects. No filename, owner, or extension in the key. | Deleted by the deletion job. |
| `staging/{uploadSessionId}` | API-proxied uploads before promotion. | Expire after 1 day. |
| `orphan/` | Objects found without a DB row during reconciliation (7-day grace). | Expire after 7 days. |
| (multipart parts) | Incomplete uploads under the final key. | Abort after 7 days (bucket lifecycle) and 24 h (worker). |

- Bucket per environment: `files-<env>-<account-id>-<region>-<suffix>`.
- Block Public Access (all four flags), no ACLs, bucket owner enforced.
- SSE-S3 by default (`AES256`); SSE-KMS with a customer-managed key optional. Every `PutObject`,
  `CreateMultipartUpload`, and `CopyObject` sets encryption explicitly even if the bucket default
  exists.
- Versioning **disabled** in production (ADR-0008); if enabled, noncurrent versions expire in 24 h.
- TLS-only bucket policy; deny `s3:*` to `*` principals; deny unencrypted puts.
- CORS: allow `PUT` from the portal origin, headers `Content-Type`, methods `PUT`, max age 1 h.
  No `GET` CORS is needed because downloads go through the API.

## 3. Upload session creation

Request (portal adds retention/tags):

```json
{
  "fileName": "informe Q3.pdf",
  "sizeBytes": 2147483648,
  "contentType": "application/pdf",
  "checksumSha256": "…64 hex…",
  "retention": { "mode": "finite", "days": 365 },
  "tags": ["finance", "q3"]
}
```

Server steps:

1. Authenticate/authorize; for integrations, verify `file.write`, active status, IP, quota.
2. Validate `fileName` (1–512 chars, no control chars, no path separators after sanitization),
   `sizeBytes` ≤ max (per integration or global), `contentType` in the allowlist and consistent
   with the extension when known.
3. Determine retention: portal → requested (if allowed) or active default; integration →
   `indefinite`, `expires_at = NULL`; snapshot `retention_policy_id`/`retention_days`.
4. Check quotas: open sessions per integration, daily bytes (`daily_usage` + open sessions),
   global storage quota.
5. Insert `files` (`pending_upload`) and `upload_sessions` (`mode = single|multipart`), owned by
   the caller. Choose multipart when `sizeBytes >= Storage__MultipartThresholdBytes` (64 MiB).
6. Create the S3 multipart upload with key `f/{fileId}`, `Content-Type`, SSE, and metadata
   (`files-file-id`, `files-upload-session-id`). Store `s3_upload_id`.
7. Generate presigned `UploadPart` URLs (first batch, typically all parts for ≤1.6 GiB) or a
   single `PutObject` URL. Sign only `Content-Type` and the exact key/upload/part.
8. Commit and return the session. If the DB commit fails after S3 initiation, abort the multipart
   upload (best effort) and let the lifecycle cleanup catch the rest.

Open-session limits: 5 per integration, 10 per user (configurable). Session TTL: 60 min,
renewable by requesting more parts (extends within limits); expired sessions are aborted.

## 4. Part sizing and presigning

| Parameter | Default |
|---|---|
| Part size | 16 MiB (min 5 MiB except last) |
| Max parts | 10,000 |
| Presign TTL | 15 min |
| URLs per request | 100 |
| Client concurrency | ≤4 recommended |

- Part count = `ceil(size / partSize)`; if >10,000, the server increases part size (up to 5 GiB
  parts) or rejects.
- Presigned URLs are returned in batches; the client calls `/parts` for the rest. Expired URLs can
  be re-presigned while the session is valid (same part number).
- The client must send the signed `Content-Type` for single PUT; multipart parts do not sign
  content type by default (S3 part uploads ignore it), but the final object's type comes from
  `CreateMultipartUpload`.

## 5. Client upload sequence (portal)

1. Validate file locally (size/type), compute SHA-256 only for files ≤100 MiB (Web Crypto requires
   buffering; skip for large files).
2. `POST /v1/portal/upload-sessions` → session + parts.
3. Upload parts with XHR/fetch (progress events), 3–4 concurrent, retry a part 3 times with
   backoff on network/5xx; re-presign on 403/expired URL.
4. On all parts complete → `POST .../complete`.
5. Poll `GET /v1/portal/files/{id}` until `available` (or `processing`), then show success.
6. Cancel → `DELETE .../{id}`; closing the tab leaves cleanup to the worker.

## 6. Completion and verification

`POST .../complete` (idempotent):

1. Load the session; require owner, state `initiated|uploading`, not expired.
2. **Multipart:** `ListParts`; verify every part from 1..N exists with non-zero size; for each
   recorded part compare size; `CompleteMultipartUpload` using the listed ETags.
   **Single:** no-op (the object already exists); if missing → `422 upload_parts_incomplete`.
3. `HeadObject`; verify:
   - `ContentLength == expected_size_bytes`,
   - `ContentType` equals the declared type,
   - metadata `files-file-id` matches,
   - `ChecksumSHA256` equals the provided checksum when supplied.
4. If malware scanning is enabled: set `processing`, `scan_status = pending`, enqueue `file.scan`;
   else set `available`, `scan_status = not_scanned`, `available_at = now()`.
5. Write parts, audit (`upload_verified`), outbox (`file.uploaded`/`file.available`), increment
   `daily_usage.bytes_uploaded`, delete the session's staging key if proxy.
6. Return the file metadata.

Failure semantics:

| Failure | Result | Cleanup |
|---|---|---|
| Missing/incorrect parts | `422 upload_parts_incomplete` | Session stays open; client fixes and retries. |
| Size mismatch | `422 upload_size_mismatch`, file `upload_failed` | Abort multipart; client re-uploads. |
| Checksum mismatch | `422 upload_checksum_mismatch`, file `upload_failed` | Abort; re-upload. |
| Type mismatch | `422 unsupported_content_type` | Abort. |
| S3 5xx | `503 dependency_unavailable` | Retry complete (idempotent). |
| Already completed | `409 upload_session_completed` | Return current file metadata (idempotent response). |

## 7. Proxy upload (integration, ≤100 MiB)

`POST /v1/integrations/files` (`multipart/form-data`):

1. Enforce `max_proxy_upload_bytes` (100 MiB) at the request limit and while streaming.
2. Stream to `staging/{uploadSessionId}` in S3 (no local disk buffering), with SSE.
3. Verify size/content type via `HeadObject`.
4. `CopyObject` to `f/{fileId}`, set metadata and SSE, then delete staging.
5. Mark `available` (or `processing` if scanning), write audit/outbox, return metadata.
6. On failure, delete staging and mark `upload_failed`.

This path exists for consumers that cannot reach S3 directly (§15.5). It is not used by the
portal. If an `Idempotency-Key` is supplied, the stored request hash uses the file name, size, and
the `checksumSha256` form field; the checksum is required for a safe replay, otherwise a retry may
create a duplicate file.

## 8. Download authorization

| Source | Authentication | Checks before grant/stream |
|---|---|---|
| Owner | Entra user token | `owner_user_id = user`, file downloadable. |
| Private link | Entra user token | Link active/not expired/limits, tenant match, file downloadable. |
| Public link | None | Token + key, abuse limits, link limits, file downloadable. |
| Integration | Entra app/API key | `file.read` + owner or active grant, file downloadable. |

### Grant flow (owner/private/public)

1. `POST` endpoint → generate 32 random bytes → `grant_token_hash = SHA-256(token)`.
2. If the link has `max_downloads`, atomically reserve a slot (see §10); otherwise skip.
3. Insert `download_grants` (`state=issued`, `expires_at=now()+120s`, source, user, IP hash, link).
4. Return `{grantToken, expiresAt, downloadUrl}`; never echo the key.
5. `GET /v1/download-grants/{grantToken}`:
   - look up by hash; require `state=issued` and not expired;
   - `UPDATE ... SET state='redeemed', redeemed_at=now() WHERE id=$1 AND state='issued'` (atomic;
     zero rows → 404);
   - re-check file downloadability (expiry may have passed);
   - stream.
6. After the response completes, mark `completed`, release the reservation, increment counts,
   write `download_events`; if the response fails/aborts, mark `failed` and release.

A grant is a bearer capability: the browser navigates to the URL without an `Authorization`
header. It is 256-bit, single-use, 120 s, `no-store`, `no-referrer`, and not logged.

**Expiry sweep:** a job runs every minute and, for grants still `issued`/`redeemed` past
`expires_at`, marks them `expired` and decrements `download_links.reserved_downloads` (never below
zero). This releases slots held by crashed clients so `max_downloads` cannot leak. The update is
idempotent and guarded by the grant state.

### Integration download

`GET /v1/integrations/files/{fileId}/content` — authorize on every request, stream, support
`Range`, count on completion. No grant; server-to-server clients can follow redirects safely.

## 9. Streaming implementation

- Open the S3 object with `GetObjectAsync` (or ranged request) **before** writing headers; map
  `NoSuchKey` to `404 file_unavailable` (and quarantine the file via an async event).
- Set `Content-Type`, `Content-Length`, `Content-Disposition` (ASCII fallback + RFC 5987),
  `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, `Accept-Ranges: bytes` (integration
  only), and `Referrer-Policy: no-referrer` (grant).
- Copy with a pooled 64 KiB buffer, honoring `RequestAborted`; do not buffer the whole object.
- On disconnect, stop reading and release resources; do not count the download unless the full
  body (or the requested range) was written.
- Never set `Content-Encoding`; preserve the stored content type.

## 10. Counting and concurrency

### Reservation

```sql
UPDATE download_links
   SET reserved_downloads = reserved_downloads + 1
 WHERE id = $1 AND status = 'active'
   AND (expires_at IS NULL OR expires_at > now())
   AND (max_downloads IS NULL
        OR successful_downloads + reserved_downloads < max_downloads)
RETURNING successful_downloads, reserved_downloads;
```

Zero rows → generic 404. The reservation is inserted with the grant in the same transaction.

### Completion

Inside a transaction: mark grant `completed` (state predicate), decrement `reserved_downloads`,
increment `successful_downloads` on the link and `successful_downloads` on the file, set
`last_download_at`, append `download_events` (`completed`) and an outbox event. A crash between
the stream and this update loses one count (`download_completion_lost_total` metric); the
reservation expires after 120 s and is released.

### Rules

- Counts increment **only** on successful stream completion (per ADR-0005).
- A grant = one counted download. Range is not supported on grants.
- Integration range responses count once per completed response (documented; informational).
- Owner downloads count and are tagged `source=owner`.
- Link `max_downloads` is enforced against `successful + reserved`; UI shows "used" as
  `successful` and "pending" as `reserved`.

## 11. Abuse controls on the public path

- Before key verification: check `abuse_lockouts` for (`link_ip`, `link`); if locked → `429`
  without hashing.
- Verify key with Argon2id; on failure insert `download_key_attempts` and compute the window
  count; apply progressive delay (`0.5/1/2/4 s`), then 15-min lockout after 5 failures.
- Soft-lock the link at 100 failures/hour and emit an alert.
- Grant endpoint rate limit 30/min/IP; metadata 60/min/IP.
- IPs are hashed with a daily pepper; raw IPs are not stored.

## 12. Limits reference

| Limit | Default | Config |
|---|---|---|
| Max file size | 5 GiB | `Storage__MaxFileSizeBytes`, per integration |
| Multipart threshold | 64 MiB | `Storage__MultipartThresholdBytes` |
| Part size | 16 MiB | `Storage__PartSizeBytes` |
| Max parts | 10,000 | fixed (S3) |
| Session TTL | 60 min | `Storage__UploadSessionTtlMinutes` |
| Presign TTL | 15 min | `Storage__PresignTtlMinutes` |
| Proxy upload | 100 MiB | `Storage__MaxProxyUploadBytes` |
| Grant TTL | 120 s | `Storage__GrantTtlSeconds` |
| Concurrent sessions | 5/integration, 10/user | `platform_settings` |
| Open multipart cleanup | 24 h | `Storage__AbortIncompleteAfterHours` |

## 13. Test matrix

| Test | Type |
|---|---|
| 0 B, 1 B, exactly threshold−1/+1, 5 GiB (sparse/streamed) uploads | Integration (LocalStack S3) |
| Multipart with out-of-order parts, retried part, expired presign | Integration |
| Size/type/checksum mismatch, missing part, aborted session | Integration |
| Presigned URL cross-key/cross-part attempt | Security integration |
| Concurrent grants with `max_downloads=1` | Concurrency |
| Grant reuse after completion/expiry | Integration |
| Expiry between grant and stream | Integration |
| Client disconnect mid-stream (no count) | Integration |
| Range requests (integration) | Integration |
| 5 GiB download memory ceiling (<200 MiB RSS) | Load |
| S3 5xx/timeout during complete/delete | Chaos/fault injection |
| Key lockout thresholds and timing | Security/perf |
