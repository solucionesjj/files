# Upload and Download Design (API-Proxied)

**Decisions:** ADR-0003, ADR-0005, ADR-0010, ADR-0011
**API:** `docs/04-api/openapi/files-api.v1.yaml`

## 1. Objectives

- Support up to 5 GiB on a single EC2 without exposing S3 to any client.
- Stream bytes through the API, stage them on EBS, verify them, and commit to S3 from the API.
- Never issue presigned URLs, S3 keys, or bucket credentials.
- Verify every object before it becomes available and scan it with ClamAV before it reaches S3.
- Count only completed downloads; enforce link limits under concurrency.

## 2. S3 layout and exclusive access

| Prefix | Purpose | Lifecycle |
|---|---|---|
| `f/{fileId}` | Final objects. No filename, owner, or extension. | Deleted by the deletion job. |
| `staging/` | Reserved for server-side staging if a future flow needs it. | Expire after 1 day. |
| `orphan/` | Objects found without a DB row (7-day grace). | Expire after 7 days. |
| `backups/postgres/` | Nightly `pg_dump` files from the host. | Expire after 35 days. |
| (multipart parts) | Incomplete uploads under the final key. | Abort after 7 days (lifecycle) / 24 h (worker). |

- Bucket per environment: `files-<env>-<account-id>-<region>-<suffix>`.
- Block Public Access (all four flags), no ACLs, owner enforced, SSE-S3, versioning off.
- **Only the EC2 instance role has object/list/multipart access.** The bucket policy denies those
  actions to every other principal (`StringNotEquals aws:PrincipalArn`), including the deployment
  role. No presigned URLs are generated. The API/worker uses the instance role (no static keys).
- No S3 CORS: the browser talks to the API only. The API still allows the configured portal
  origin (for local `ng serve`); in production Caddy serves the SPA same-origin.

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

1. Authenticate/authorize; for integrations verify `file.write`, active status, IP, quota.
2. Validate `fileName`, `sizeBytes` ≤ max, content type allowlist, and `sizeBytes ≥ 0`.
3. Determine retention: portal → requested/default (30 days; >default up to 365 requires a
   `retention.reason` note); integration → `indefinite`, `expires_at = NULL`.
4. Check quotas: open sessions per integration/user, daily bytes, S3 storage, and **staging
   capacity** (`free_bytes - reserve >= expected_size + 64 MiB`).
5. Compute chunk geometry: `chunkSize = 16 MiB`, `chunkCount = max(1, ceil(size/chunkSize))`,
   `chunkCount ≤ 10,000`.
6. Insert `files` (`pending_upload`) and `upload_sessions` with a unique staging path
   `/var/lib/files/staging/{sessionId}.part`; create the file with `O_EXCL` and `0600`.
7. Commit the transaction and return `{fileId, uploadSessionId, chunkSizeBytes, chunkCount, expiresAt,
   receivedBytes: 0}`. **No S3 call is made at this point.**

If the DB commit fails after file creation, the temp file is removed; the cleanup job also catches
orphans.

## 4. Chunk upload

`PUT /v1/{portal|integrations}/upload-sessions/{sessionId}/chunks/{chunkNumber}`

- Body: raw bytes (`application/octet-stream`), `Content-Length` required, ≤ `chunkSizeBytes`.
- Optional `X-Chunk-SHA256`; when present it is verified before the bytes are committed.
- `chunkNumber` in `1..chunkCount`; the offset is `(chunkNumber-1) * chunkSizeBytes`.
- The last chunk may be smaller; a session with `sizeBytes = 0` has one zero-length chunk.
- Chunks may arrive out of order and in parallel (client ≤2 in flight by default).
- Ownership is checked on every request (session initiator).
- The API writes with `pwrite` at the offset, fsyncs, then records the chunk row and increments
  `received_bytes` atomically.
- Idempotency: a repeated chunk number with the same size/hash returns success; different
  bytes/size returns `409 chunk_conflict`.
- A chunk beyond the declared size or chunk count returns `422 chunk_out_of_range`; an oversized
  body returns `413 chunk_too_large`; a full disk returns `507 staging_capacity_exceeded`.
- `GET .../upload-sessions/{id}` returns the received chunk numbers and bytes for resume.

The session becomes `received` when `received_bytes == expected_size_bytes`; it is not complete
until `complete` verifies and commits to S3.

## 5. Completion, verification, and S3 commit

`POST .../upload-sessions/{id}/complete` (idempotent):

1. Lock the session `FOR UPDATE`; require initiator, state `received|failed|committing`, not
   expired.
2. Verify all `chunk_count` rows exist, offsets/sizes are consistent, and the sum equals
   `expected_size_bytes`; optional whole-file SHA-256 is computed by streaming the staging file.
3. If verification fails: `422 upload_incomplete`/`upload_checksum_mismatch`; the session remains
   resumable until expiry.
4. Set the upload session to `committing` and the file to `processing` (or commit immediately when
   scanning is disabled, see below).
5. **Scanning enabled:** enqueue `scan.commit`; return file metadata with `status=processing`. The
   worker streams the staged file to ClamAV (`clamd` INSTREAM):
   - **infected:** set `quarantined`, `scan_status=infected`, delete the staging file/chunks, audit
     and alert; **never upload to S3**.
   - **clean:** set `scan_status=clean`; upload the staged file to S3 (multipart 64 MiB parts, or
     `PutObject` below 64 MiB); `HeadObject` verifies size, content type, metadata and encryption;
     set `available`, write audit/outbox, delete staging/chunks.
   - **scanner unavailable/timeout:** keep `processing`/`scan_status=failed`, retry with backoff and
     alert; the file is not downloadable and staging is retained.
6. **Scanning disabled:** the API uploads the staged file to S3 immediately, verifies it, sets
   `available`, and deletes staging/chunks.
7. **S3 failure:** keep the file `processing`/session `committing`, retain staging and chunks,
   record sanitized `last_error`, retry the commit with backoff. `POST /complete` returns the
   current metadata (`processing`) and is idempotent; do not re-upload chunks.
8. The worker marks the upload session `completed` only after the S3 object is verified (or the
   file is quarantined). Once `complete` succeeds, the session no longer expires at the 60-minute
   upload TTL; the scan/commit job owns it and retries for up to 24 hours before failing it.

## 6. Staging disk management

- Host EBS: 100 GiB gp3 (default), mounted at `/var/lib/files`; PostgreSQL data and staging live
  on separate subdirectories.
- Reserve: 10 GiB free at all times (`Storage__StagingReserveBytes`).
- Max concurrent sessions per host: 3; per integration/user: configurable (default 3).
- Temp files are `0600`, owned by the API container user, under a directory not served by Caddy.
- A worker job every 10 minutes removes expired/aborted staging files, fails sessions older than
  their TTL, and aborts S3 multipart uploads older than 24 h (lifecycle is the backstop at 7 days).
  It must **not** delete a `processing` file's staging until the scan/commit job has finished or
  reached its retry/timeout limit.
- ClamAV: `clamd` on port 3310 (internal), signatures on a dedicated volume, `StreamMaxLength` /
  `MaxFileSize` set above 5 GiB, one concurrent scan (configurable) to bound memory/CPU.
- Alerts: disk >80% warning, >90% critical, staging reserve breached, write failures.

## 7. Download authorization

| Source | Authentication | Checks before grant/stream |
|---|---|---|
| Owner | Entra user token | `owner_user_id = user`, file downloadable. |
| Private link | Entra user token | Link active/not expired/limits, tenant match, file downloadable. |
| Public link | None | Token + key, abuse limits, link limits, file downloadable. |
| Integration | Entra app token only | `file.read` + owner or active grant, file downloadable. |

### Grant flow (owner/private/public)

1. Generate 32 random bytes; store `SHA-256(token)`.
2. If the link has `max_downloads`, atomically reserve a slot.
3. Insert `download_grants` (`state=issued`, TTL 120 s, source, user, IP hash, link).
4. Return `{grantToken, expiresAt, downloadUrl}`; never echo the key.
5. `GET /v1/download-grants/{grantToken}` consumes the grant atomically, rechecks file state, and
   streams from S3.
6. On completion, mark `completed`, release the reservation, increment counts, and write
   `download_events`; on failure/abort, mark `failed` and release.

A grant is a bearer capability (browser navigation needs no Authorization header), 256-bit,
single-use, 120 s, `no-store`, `no-referrer`, never logged.

New links get a server-assigned expiry: public **15 days**, private **30 days** (admin-configurable;
owner cannot change). Private links also enforce their ACL: specific Entra users/groups or
`allow_all_tenant`; a user outside the ACL receives the generic `404 link_not_found`.

### Integration download

`GET /v1/integrations/files/{fileId}/content` — authorize on every request, stream, support
`Range`, count on completion.

## 8. Streaming implementation

- Open the S3 object before writing headers; map `NoSuchKey` to `404 file_unavailable` and emit a
  reconciliation event.
- Set `Content-Type`, `Content-Length`, `Content-Disposition` (ASCII fallback + RFC 5987),
  `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, `Referrer-Policy: no-referrer`,
  and `Accept-Ranges: bytes` (integration only).
- Copy with a pooled 64 KiB buffer, honor `RequestAborted`, never buffer the whole object.
- On disconnect, stop reading and release resources; do not count the download unless the full
  body was written.
- Never set `Content-Encoding`; preserve the stored type.

## 9. Counting and concurrency

Reservation SQL:

```sql
UPDATE download_links
   SET reserved_downloads = reserved_downloads + 1
 WHERE id = $1 AND status = 'active'
   AND (expires_at IS NULL OR expires_at > now())
   AND (max_downloads IS NULL
        OR successful_downloads + reserved_downloads < max_downloads)
RETURNING successful_downloads, reserved_downloads;
```

Completion, in one transaction: mark grant `completed` (state predicate), decrement
`reserved_downloads`, increment link/file counts, set `last_download_at`, append
`download_events`, outbox event. A crash between stream and update loses one count
(`download_completion_lost_total`); reservations expire after 120 s via the grant-expiry job.

Rules: counts increment only on successful completion; a grant is one counted download; range is
not supported on grants; integration range responses count once per completed response; owner
downloads use `source=owner`.

## 10. Abuse controls (public path)

- Check `abuse_lockouts` before hashing; locked → `429` without Argon2.
- Wrong key: record attempt, progressive delay 0.5/1/2/4 s, 15-min lockout after 5 failures/link+IP;
  soft-lock at 100 failures/link/hour + alert.
- Grant endpoint 30/min/IP; metadata 60/min/IP; IPs hashed with a daily pepper.
- Argon2id m=19 MiB, t=2, p=1; bounded concurrency.

## 11. Limits reference

| Limit | Default | Config |
|---|---|---|
| Max file size | 5 GiB | `Storage__MaxFileSizeBytes`, per integration |
| Client chunk | 16 MiB | `Storage__ChunkSizeBytes` |
| Max chunks | 10,000 | `Storage__MaxChunkCount` |
| S3 part | 64 MiB | `Storage__S3PartSizeBytes` |
| Single PutObject threshold | 64 MiB | `Storage__S3SinglePutThresholdBytes` |
| Session TTL | 60 min | `Storage__UploadSessionTtlMinutes` |
| Staging reserve | 10 GiB | `Storage__StagingReserveBytes` |
| Concurrent sessions | 3/host | `Storage__MaxConcurrentSessions` |
| Grant TTL | 120 s | `Storage__GrantTtlSeconds` |
| Cleanup | 24 h stale / 7 d lifecycle | `Storage__AbortIncompleteAfterHours` |
| Request body (Caddy) | 20 MiB | `deploy/Caddyfile` |

## 12. Test matrix

| Test | Type |
|---|---|
| 0 B, 1 B, exactly chunk boundaries, 5 GiB chunked upload | Integration (LocalStack) |
| Out-of-order chunks, duplicate chunk, conflicting chunk | Integration |
| Chunk hash mismatch, oversized chunk, out-of-range chunk | Integration |
| Disk reserve breach (`507`), write failure | Integration/chaos |
| Complete before all chunks, size mismatch, checksum mismatch | Integration |
| S3 commit failure then retry without re-upload | Integration/fault injection |
| Session expiry cleanup, orphan staging file cleanup | Integration |
| No presigned headers/URLs in any response | Contract/security |
| 5 GiB download RSS <200 MiB | Load |
| Clean file scanned before S3 | Integration (ClamAV) |
| Infected file quarantined and absent from S3 | Integration/security |
| Scanner unavailable → retry, file not downloadable | Chaos |
| Scan disabled path commits directly | Integration |
| Range requests (integration) | Integration |
| Concurrent grants with `max_downloads=1` | Concurrency |
| Grant reuse/expiry | Integration |
| Client disconnect mid-stream (no count) | Integration |
| Key lockout thresholds/timing | Security/perf |
