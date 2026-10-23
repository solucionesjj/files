# ADR-0003 — S3 is exclusive to Files: API-proxied chunked uploads and streamed downloads

- **Status:** Accepted (supersedes the presigned-upload design)
- **Date:** 2026-10-23
- **Deciders:** Architecture, Security, Operations

## Context

The initial deployment is a single EC2 instance plus one S3 bucket. The product requirement is
that **only Files may consult or manage the bucket**; no browser, integration, or human may access
S3 directly. The source specification still requires private storage, 5 GiB files, short-lived
authorization for transfers, verification of uploaded objects, and downloads that are counted
only when completed. The previous design used presigned URLs for browser/consumer uploads; that is
incompatible with strict S3 exclusivity.

## Decision

**Uploads: client → Files API → local staging → S3 (server-side)**

1. `POST /v1/{portal|integrations}/upload-sessions` authenticates/authorizes, validates size,
   content type, quota, and free disk, then creates a `pending_upload` file and an upload session
   with a local staging path (`/var/lib/files/staging/{sessionId}.part`). No S3 call is made.
2. The client `PUT`s fixed-size chunks (16 MiB default) to
   `/upload-sessions/{id}/chunks/{n}`. The API writes each chunk at its offset, records a
   per-chunk hash, and rejects conflicts, out-of-range chunks, or oversized bodies.
3. `POST .../complete` verifies every chunk and the total size (and optional whole-file SHA-256),
   then the API itself uploads the staged file to S3 using multipart upload (64 MiB parts) or a
   single `PutObject` below the threshold, and verifies the object with `HeadObject`.
4. The temp file is deleted after S3 verification. Retrying `complete` after an S3 failure reuses
   the staged file, so the client does not re-upload 5 GiB.
5. `DELETE .../upload-sessions/{id}` aborts and removes the staging file. A worker job removes
   expired sessions and orphaned temp files and aborts stale S3 multipart uploads.

**Downloads:** clients stream through the API with a 120-second single-use grant (ADR-0005); no
presigned GET URLs.

**S3 access control:** the only principal with object/list/multipart permissions is the EC2
instance role used by the API and worker. A bucket policy **denies** those actions to every other
principal (`StringNotEquals aws:PrincipalArn`). The bucket has Block Public Access, SSE-S3, no
ACLs, no versioning, and lifecycle rules for `staging/`, `orphan/`, `backups/`, and incomplete
multipart uploads. Infrastructure automation may manage bucket configuration, but not read or
write objects. No presigned URLs are issued anywhere.

Object keys remain `f/{fileId}`, `staging/` (only if a future server-side copy needs it),
`orphan/`, and `backups/`; the API/worker is the only writer/reader.

## Consequences

- Upload traffic and staging disk now consume EC2 resources. At 5 GiB, the API streams chunks and
  the S3 commit reads from EBS; disk capacity and IOPS must be sized (100 GiB gp3 default, 10 GiB
  reserve, max 3 concurrent sessions).
- The API must enforce request size limits (chunk 16 MiB, Caddy 20 MiB), disk quota, and
  per-session concurrency; a full disk returns `507 staging_capacity_exceeded`.
- The client cannot bypass the API, so ownership/authorization is checked on every chunk.
- Upload sessions are resumable (`GET` status) and complete is idempotent; retries after network
  failures are safe.
- The browser no longer needs S3 CORS. The SPA is same-origin behind Caddy in production and uses
  the dev origin in local development.
- Upload throughput is bounded by one EC2's network and disk; horizontal scaling is a later
  phase.
- If the instance fails before the S3 commit, staged chunks are lost; the client restarts the
  session. Once the object is in S3, downloads survive instance replacement.

## Alternatives considered

- **Presigned multipart URLs** (previous design): rejected because clients would talk to S3
  directly, violating the exclusive-access requirement, and because it exposes capability URLs.
- **Browser → API → S3 streaming without local staging:** rejected for 5 GiB because a failed S3
  commit forces a full client re-upload and holds a request open for a long time; staging makes
  commit retries cheap and enables verification before S3.
- **S3 Access Points / VPC endpoint policies:** still S3 access by the instance role and do not
  prevent other principals in the account; the bucket policy deny is the control.
- **MinIO on EC2 instead of S3:** rejected; the bucket is the mandated durable store and the
  instance is not a backup target.
