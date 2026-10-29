# Session Log

Append one entry per work session. Keep it short: what changed, what was validated, what is next,
and any decision that future sessions must know. Do not rewrite history; correct it with a new
entry.

---

## 2026-10-29 — S13/M7 implemented: operations readiness

**Delivered (code + tests green: 299 unit + 12 contract + 6 new integration; drills executed)**

- **Deploy + rollback (gate 1/ops):** `deploy/deploy.sh` (idempotent: `git fetch`+checkout → ensure
  PostgreSQL → `backup.sh` + capture S3 key → explicit Flyway `migrator` (`--profile tools`, never
  auto) → SPA build in `node:22-alpine` + rsync → `compose up -d --build api worker caddy` →
  `smoke.sh` → write `/opt/files/REVISION`) and `deploy/rollback.sh` (previous ref + optional
  pre-deploy dump restore). `deploy/README.md` and `deployment-and-rollback.md` refreshed with the
  exact reproducible invocation.
- **Backup/restore drill (gate 5):** `deploy/drill-backup-restore.sh` — disposable PostgreSQL 18 +
  LocalStack S3, `pg_dump|gzip → _latest` → `pg_restore`; seeded rows round-trip (users/integrations/
  files/audit = 1/1/1/1, `flyway_schema_history`=6). **RTO 338 ms** vs SLO ≤4 h; RPO 0 (production
  RPO 24 h nightly). Recorded in the runbook Drill log.
- **Migration/rollback rehearsal (gate 9):** `deploy/drill-migrations.sh` — Flyway V1→V6 from empty
  in ~2 s (6/6), then dump→restore round-trip leaves 29 tables + `flyway_schema_history`=6 (a restore
  is a valid rollback path; no down-migrations, expand/contract preferred). Recorded in the Drill log.
- **Dashboards + alerts (gate 6):** `deploy/observability/` — `signoz-alerts.yaml` (18 PromQL rules,
  each with severity/runbook/dashboard + `files-oncall` route), `signoz-dashboards.yaml` (six
  dashboards per observability-and-slos.md §3), and `README.md` (import steps, on-call rota, the
  alert→runbook→action map, metric sources). `deploy/otel-collector.yaml` now also collects
  host (`hostmetrics`) and container (`docker_stats`) metrics; the compose `observability` profile
  mounts the read-only Docker socket.
- **Runbook rehearsal:** walked First response / host-down / disk-pressure / backup-failure /
  deletion-failures / database; fixed drift — the backup runbook no longer `source`s the `.env`
  (connection strings contain `;`), `aws s3 ls` now exports the static key, deletion/database SQL
  gets a `$DC exec -T postgres psql` invocation, and `deployment-and-rollback.md` §6 now matches the
  actual unauthenticated `smoke.sh` (authenticated checks documented as manual/CI).
- **Cost review (gate 7):** `cost-model.md` §8 reconciled with the pre-existing host/S3 (EC2/EBS/EIP
  fixed; S3 storage/egress/requests + EBS growth are the variable drivers), budget `$115/mo`, AWS
  Budgets at 80/100/120% → `files-oncall`, plus a monthly reconciliation table.
- **Open item — admin surface (resolved):** implemented the **read-only** admin/auditor surface
  contract-first — `GET /v1/admin/audit-events`, `/v1/admin/download-events`, `/v1/admin/usage`,
  `/v1/admin/settings` (`AdminAuditEndpoints`, `IAuditLogRepository`/`IDownloadEventRepository`/
  `IUsageRepository` + Dapper SQL, `AdminOrAuditor` policy, `AdminAuditReadTests`). This closes
  FR-ADM-005/006/008 + FR-AUD-001..003 (auditors now have a read endpoint; no content access).
- **Decision (deferral):** the **mutating** admin ops — `PATCH /admin/settings`,
  `POST /admin/files/{id}/deletion`, and integration grant management
  (`GET/POST /admin/files/{id}/grants`, `DELETE /admin/grants/{id}`) — are **deferred to v1.1**
  (post-launch, target 2026-11). Removed from the OpenAPI contract, traceability, and threat model so
  the docs no longer claim an unimplemented surface; `SecuritySurfaceTests`/`ContractSurfaceTests`
  now lock the read-only surface and assert the deferred mutations are absent. The mutating part of
  FR-ADM-007 (admin file deletion) and settings PATCH remain open and are tracked in
  `docs/01-product/open-questions.md`.

**Validated:** `dotnet build -warnaserror` clean; 299 unit + 12 contract + `AdminAuditReadTests`
(6) green; `check-security-static.sh` stays green (all new admin routes are `MapGet`); Redocly
lint green after the contract shrink; both drill scripts run end-to-end.

**Next:** S14/M7 — launch (staging sign-off, production cutover, post-launch monitoring).

---

## 2026-10-29 — S12/M7 implemented: test hardening and security

**Delivered (code + tests green: 291 unit + 9 contract + integration incl. new matrix/tampering)**

- **Authorization + IDOR matrix (NFR-005, I-01, E-01):** `AuthorizationMatrixTests`
  ([tests/Files.IntegrationTests/Upload/AuthorizationMatrixTests.cs](../../tests/Files.IntegrationTests/Upload/AuthorizationMatrixTests.cs))
  — one table-driven suite over every implemented route × 8 actors (anonymous / user-owner /
  user-other / auditor / admin / integration-owner / integration-other / integration-without-scope),
  asserting the exact status + `code` and the canonical 404-for-foreign vs 403-for-insufficient
  role/scope split (ADR-0012/0014). Includes the denied cases: admins never read content (404 on
  foreign file download-grant/content, D-19) and auditors are read-only with no download
  (403 on every user/admin route, FR-ADM-008); machine scopes are intersected (read-only key → 403
  `insufficient_permission` on write/delete).
- **Upload tampering (T-01/02/05, FR-UP):** `UploadTamperingTests` — filename path traversal and
  CRLF/control rejection, content-type header injection, negative size, zero-size (valid empty file),
  oversized (chunk-count and per-integration max file size → 422/413), oversized chunk, and
  whole-file checksum mismatch. Extracted `Files.Application.Storage.ContentDisposition` (shared by
  the grant + integration download paths) and unit-tested RFC 5987 behavior
  (`ContentDispositionTests`): ASCII fallback never breaks/injects, `filename*=UTF-8''` preserves the
  name. EICAR→quarantine and scanner-down→unavailable remain covered by `ScanCommitTests`.
- **Secret/log-leakage guard (I-03):** `LogRedactionTests` (reflects over `LoggerMessage` templates)
  + `scripts/check-security-static.sh` — no `DateTime.UtcNow`, no presigned S3, no Graph/POP, no
  `innerHTML`/`bypassSecurityTrust` in the SPA, no log template emitting a filename/key/token/email/
  object key, and no mutating route mapped to audit/usage/download-event resources. Wired into
  `make validate` and CI.
- **Secret + dependency/image + SAST scanning (E-04):** gitleaks (`.gitleaks.toml` allowlist = the
  two committed placeholder templates only; `make scan-secrets`), Trivy (`make scan-deps` /
  `scan-images`), plus the existing `dotnet list package --vulnerable` + analyzer set as the SAST
  baseline — all wired into `.github/workflows/ci.yml` (`security-scanners` job). Baseline recorded
  in [security-scanning.md](../07-quality/security-scanning.md); no real secret is committed.
- **ZAP baseline (S-04):** `scripts/zap-baseline.sh` (baseline + documented authenticated active-pass
  procedure + artifacts location) and a CSP header added to `deploy/Caddyfile`
  (`script-src 'self'`, `object-src 'none'`, `frame-ancestors 'none'`, Entra-limited connect/frame).
  The SPA XSS posture is locked by the static guard (§ innerHTML) — filenames are interpolation-only.
- **Load/soak (D-01/02/06, NFR-003/004):** k6 scenarios in `tests/perf/` (metadata reads, chunked
  upload, ranged download, public-link redemption/abuse, 5 GiB upload/download smoke) encoding the
  SLO thresholds from `observability-and-slos.md`; `make perf-smoke` (CI `performance-smoke` job)
  validates every scenario and runs a 1-VU pass; the full 2×-baseline run is documented in
  `tests/perf/README.md`.
- **Audit append-only + no-admin-content (T-04, FR-AUD-002, D-19):** `SecuritySurfaceTests`
  (contract) asserts the audit/download-event resources have no mutating operation and no admin
  operation returns content; the runtime denial is asserted by `AuthorizationMatrixTests`.

**Decision:** the contract still declares `/admin/audit-events`, `/admin/download-events`,
`/admin/usage`, `/admin/settings`, and `/admin/files/{id}/deletion` + `/admin/files/{id}/grants`
that are **not implemented** (deferred to S13/S14; auditors currently have no read endpoint). The
matrix and `SecuritySurfaceTests` cover the implemented surface and treat those contract paths as
read-only-by-declaration. `Content-Disposition` was extracted from two duplicated private helpers
into `Files.Application.Storage.ContentDisposition` so the RFC 5987 behavior is unit-testable.

**Next:** S13/M7 — operations readiness (deploy to the existing host, backup/restore drill,
dashboards/alerts, runbook rehearsal, cost review).

---

## 2026-10-29 — S11/M6 implemented: physical deletion, tombstones, reconciliation, metrics

**Delivered (code + tests green: 282 unit + 106 integration + 5 contract)**

- **Physical deletion job (FR-DEL-001..005/007/010, ADR-0008):** `DeletionService` + `DeletionJob`
  (every 5 min, `FOR UPDATE SKIP LOCKED`, batch 100) claims due `pending_deletion` files
  (`next_deletion_at <= now`, `legal_hold=false`), calls S3 `DeleteObjectAsync` (idempotent:
  `NoSuchKey`/404 = success), and either marks `deleted` (`deleted_at`, audit `deleted`, outbox
  `file.deleted`) or records a retryable/permanent failure. Retryable (5xx/429/408/timeout) →
  `deletion_attempts++`, `next_deletion_at = now + min(1h, 1m·2^(attempts-1))` ±20% jitter,
  sanitized `last_deletion_error`; permanent (403/`NoSuchBucket`) → alert (error log + metric), no
  backoff. Legal hold is re-checked right before the delete and every mutation is guarded on
  `status='pending_deletion' AND legal_hold=false` (a held file can never be physically deleted).
  Owner/admin/integration `pending_deletion` rows (S7/S9) flow through the same pipeline.
- **Tombstones + retention purge (FR-DEL-008, ADR-0008):** `TombstonePurgeService` + `TombstonePurgeJob`
  null `storage_key` on `deleted` rows older than 60 months, retaining all other tombstone metadata;
  audited `tombstone_purged`. `V6` makes `files.storage_key` nullable for this. The backups caveat
  (logical `pg_dump` retains copies ≤35 days) is documented — no immediate-erasure claim.
- **Reconciliation (FR-DEL-006):** `ReconciliationService` + `ReconciliationJob` (daily): DB→S3
  keyset-pages `available` files and `HeadObject`s them — missing → `quarantined`
  (`quarantine_reason='object_missing'`), links revoked, audited, alert; S3→DB uses bounded
  `ListObjectsV2` — `f/` objects with no file row move to `orphan/` (copy+delete) and `staging/`
  objects are deleted (staging is local disk per ADR-0003, so they are orphaned by construction).
  Fail-closed: a failed HEAD never quarantines, and reconciliation never restores availability.
  Object keys are only ever logged by fingerprint (`ReconciliationScope.Fingerprint`).
- **Storage (FR-DEL-003, NFR-015):** `IObjectStore` gains `DeleteObjectAsync` (idempotent),
  bounded `ListObjectsAsync`, and `CopyObjectAsync`; `S3ObjectStore` implements them
  (DeleteObject/ListObjectsV2/CopyObject; LocalStack-compatible). Provider errors are translated to
  a sanitized `StorageException` (`StorageErrors`); no key/filename is ever in the message.
- **Metrics/alerts (NFR-011, workflow §9):** `LifecycleMetrics` (System.Diagnostics.Metrics Meter
  `Files.Lifecycle`, registered via `AddMeter` in API + worker) — `files_expired_total`,
  `files_deleted_total`, `files_deletion_failures_total{reason}`,
  `notification_delivery_failures_total`, `reconcile_missing_db_total`, `reconcile_orphan_s3_total`,
  `reconcile_quarantined_total`, and a `job_last_success_timestamp_seconds` gauge per job (wired into
  all lifecycle jobs).
- **Config:** `SchedulerOptions` + `ConfigurationValidator` extended with `DeletionSeconds`,
  `ReconcileSeconds`, `PurgeSeconds`, `DeletionMaxAttempts`, `DeletionBatchSize`,
  `ReconcileBatchSize` (key names only). No auto-migrate; `V6__deletion_reconciliation.sql`.
- **Tests:** unit `DeletionBackoffTests` (cap/jitter bounds), `StorageErrorsTests` (sanitize +
  retryable classification), `ReconciliationScopeTests` (scope/predicate/fingerprint),
  `ConfigurationValidatorTests` (new keys); integration `DeletionReconciliationTests` (PostgreSQL 18
  + LocalStack S3, following the S10 fixture/reset pattern): DeleteObject success → tombstone,
  NoSuchKey idempotent success, retryable backoff + attempts, permanent-failure alert, legal-hold
  skip, owner/admin/integration delete flows, tombstone purge nulls `storage_key`, missing-object
  quarantine, and `f/` orphan move + `staging/` orphan delete. Mapping test stays at 19 (no new
  tables).

**Decision:** cron alignment (02:00 reconcile / 03:00 purge) is expressed as daily periodic timers
(`ReconcileSeconds`/`PurgeSeconds` = 86400) consistent with the S5/S10 BackgroundService pattern;
Quartz/advisory locks remain deferred per ADR-0007. Tombstone purge nulls `storage_key` (made
nullable by `V6`) rather than a sentinel, so `FileEntity.StorageKey` is now `string?`.

**Next:** S12 — test hardening and security (IDOR/auth matrices, upload tampering, ZAP,
dependency/IaC scans, pen-test fixes, load/soak).

---

## 2026-10-29 — S10/M6 implemented: retention, legal hold, and notifications

**Delivered (code + tests green: 242 unit + 97 integration + 5 contract)**

- **Admin retention policy (FR-RET-001/003/007, FR-ADM-003/007):** `GET/POST
  /v1/admin/retention-policy` — `GET` returns current + history; `POST` publishes a new active
  version (deactivates the current row, then inserts; the `uq_retention_policies_active` partial
  unique index enforces one active), rejects `allowIndefinite` for humans (422
  `retention_indefinite_not_allowed`) and `maxDays < defaultDays`. Audited + outbox.
- **Bulk retention migrations (FR-RET-003/007, FR-ADM-003, ADR-0006):** `POST
  /v1/admin/retention-migrations` (202) + `GET .../{migrationId}`; `RetentionMigrationService`
  claims the oldest pending row (`FOR UPDATE SKIP LOCKED`), applies the target policy default to
  human `available` non-held files in keyset pages (uuidv7 `id` cursor), recalculates notifications
  per file (supersede + schedule), and audits each with `admin_retention_migration`. Integration
  files always skipped (v1).
- **Legal hold (FR-DEL-010, FR-ADM-007):** `POST /v1/admin/files/{fileId}/legal-hold` place/release
  (reason required, optional `reviewAt`), audited (file + admin) + outbox; place supersedes pending
  notifications. The expiration job and every delete path skip `legal_hold=true`.
- **Admin retention override (FR-RET-006, FR-ADM-007):** `POST /v1/admin/files/{fileId}/retention`
  — finite-only (rejects indefinite), ≤ active policy max (365 absolute), reason required, legal
  hold → 409; recalculates notifications (supersede + schedule) via `NotificationTiming`.
- **Notification policy + email templates (FR-NOT-001/003, FR-ADM-004):** `GET/PUT
  /v1/admin/notification-policy` (versioned, intervals 1..365) and `GET /v1/admin/email-templates`
  + `PUT .../{key}/{locale}` (versioned, single active per key+locale; new version supersedes).
- **Notification pipeline (FR-NOT-002/004/005/006/007/008/009/010, ADR-0007/0009):**
  `NotificationScheduler` (idempotent upsert on `(file_id, interval_days, expiration_at)`, skips
  retroactive intervals, revives only `superseded`), `NotificationSender` (claims due rows
  `FOR UPDATE SKIP LOCKED`, reclaims `sending` >10 min, renders the recipient-locale template with
  the strict variable allowlist, sends via `IEmailSender`, records
  `notification_delivery_attempts`; provider-accepted is distinct from delivered). `SmtpEmailSender`
  (MailKit, SMTP AUTH 587 STARTTLS; POP never used); retries 1/5/15 m + 1/6 h backoff, 5 attempts
  max; transient vs permanent failures distinct. No email/credential is ever logged.
- **Expiration job (FR-RET-008, FR-DEL-001/002/005, first cut):** `ExpirationService` transitions
  past-expiry `available` files to `pending_deletion` (skipping legal hold), revokes links, cancels
  notifications, audits `expired` + outbox `file.expired`. Physical S3 deletion is S11.
- **Config:** `SmtpOptions` (host/port=587/username/`AppPassword` secret from env, `Smtp__Password`
  legacy alias; never logged) + `SchedulerOptions`; `ConfigurationValidator` extended (key names
  only). `V5__retention_notifications.sql` (SMTP provider default, candidate/sending indexes). New
  EF entities `EmailTemplate` + `RetentionMigration`; Dapper repos for notification/expiry/migration.
- **Tests:** unit `NotificationTimingTests`/`NotificationRetryTests`/`EmailTemplateRendererTests`/
  `SmtpOptionsTests` + `RetentionRulesTests` (indefinite); integration `RetentionNotificationsTests`
  (policy versioning, legal hold blocks expiry + release, bulk migration + supersede, scheduling
  dedupe + SMTP delivery via Mailpit, retry/attempt states, notification policy + template
  versioning); `ContractSurfaceTests` guards the admin retention/notification surface; mapping
  test bumped to 19 mapped entities.

**Decision:** the SMTP secret key is `Smtp__AppPassword` (ADR-0009's `Smtp__Password` accepted as a
legacy alias). Background jobs use the existing `BackgroundService` + `FOR UPDATE SKIP LOCKED`
pattern (Quartz/advisory locks remain deferred, consistent with S5); the retention/notification
rules that live in SQL are mirrored by the pure `NotificationTiming` helper for unit-testability.

**Next:** S11 — physical S3 deletion job, tombstones, reconciliation, retention purge, and
metrics/alerts.

---

## 2026-10-29 — S9/M5 implemented: integration API (machine-to-machine)

**Delivered (code + tests green: 229 unit + 91 integration + 3 contract)**

- **Contract-first (OpenAPI):** added `IntegrationFile`/`IntegrationFileList` schemas (no storage
  keys/buckets/URLs, no portal/admin fields), a `status` filter on `listIntegrationFiles`, the
  `Idempotency-Key` parameter on `completeIntegrationUpload`/`deleteIntegrationFile`, and the
  `416` response + `Content-Range` header on `downloadIntegrationFile`. Redocly stays green.
- **Endpoints (`IntegrationFilesEndpoints`, on top of the S3b/S5 API-key auth + chunk upload):**
  `GET /v1/integrations/files` (owner-scoped, newest-first keyset cursor + `status` filter),
  `GET /v1/integrations/files/{fileId}` (owned **or** active `integration_file_grants`; foreign →
  404 `file_not_found`), `DELETE` (owner-only, logical `pending_deletion`, idempotent, legal-hold
  409, audited + outbox), `GET .../availability` (200 `available`/`unavailable`, no content), and
  `GET .../content` (streamed by the API, single `Range` 206/416, `Accept-Ranges`/`Content-Range`,
  `no-store`/`nosniff`, completion-only counting + `download_events` source/actor = integration;
  no presigned URLs). New `IntegrationReadPolicy` (`file.read`) and `IntegrationDeletePolicy`
  (`file.delete`) scopes.
- **Idempotency (FR-INT-007):** `IdempotencyMiddleware` (runs **after** authorization so the API-key
  scheme has authenticated) + Dapper `IIdempotencyRepository` over `idempotency_records`; scoped per
  integration, 24 h, request-hash over method+path+body. Same key+body replays the stored response
  (status/body/headers); different body → 422 `idempotency_key_reuse`; in-flight → 409
  `idempotency_in_progress`; 5xx responses are abandoned so a retry re-runs (e.g. retryable
  `upload_commit_failed`).
- **Quotas (FR-INT-010):** `IntegrationQuotaService` reads `max_file_size_bytes`,
  `max_upload_sessions_per_integration`, `daily_upload_quota_bytes`, `daily_download_quota_bytes`
  from `platform_settings` (fallback to `StorageOptions`/defaults), and enforces max file size
  (413 `file_too_large`), concurrent open sessions (429 `quota_exceeded`), and daily upload/download
  bytes (429 `quota_exceeded`); `daily_usage` is incremented only after the upload is committed/
  verified (in `UploadCommitter.FinalizeAsync`) or the download stream completes.
- **Data model:** mapped `integration_file_grants` as `IntegrationFileGrant` (EF) with an
  `IsActive(now)` predicate; `idempotency_records`/`daily_usage` stay Dapper-only.
- **Tests:** unit `IdempotencyKeysTests`, `IntegrationQuotaTests`, `HttpRangeTests`,
  `IntegrationFileAccessTests`; integration `IntegrationApiTests` (upload→list→get→availability→
  content→delete, foreign-404 + grant-read-only, idempotent create/complete/delete, Range 206/416,
  completion-only counting + `daily_usage`, concurrent-session and daily-byte quotas); contract
  `ContractSurfaceTests` guards the integration surface.

**Decision:** `max_file_size_bytes` surfaces as **413 `file_too_large`** (a single-file validation,
per the existing contract/error catalog), while accumulated usage (sessions + daily bytes) surfaces
as **429 `quota_exceeded`**. Availability returns `200` with `state` for accessible files; the
**content** endpoint returns 404 `file_unavailable` for accessible-but-not-downloadable files.

**Next:** S10/M6 — retention, legal hold, and notifications.

---

## 2026-10-28 — S8/M4 implemented: public links and downloads

**Delivered (code + tests green: backend 13 new integration + 19 new unit, Angular build green, Playwright links spec)**

- **Public-link lifecycle (contract-first, extending the S7 grant machinery):** `GET/POST
  /v1/portal/files/{fileId}/links` (owner-scoped list with keyset cursor + create), `GET
  /v1/portal/links` (links created by caller, optional `status` filter), `DELETE
  /v1/portal/links/{linkId}` (revoke + optional `reason`, invalidates outstanding grants via raw-SQL
  grant failure + reservation release), `GET /v1/portal/links/{linkId}/stats`, `GET
  /v1/public/download-links/{token}` (anonymous `LinkPublicInfo`), `POST
  /v1/public/download-links/{token}/downloads` (key → 256-bit single-use 120 s `source=public_link`
  grant), and `GET /v1/download-grants/{token}` extended to accept `link_id != null` grants with a
  link-active re-check.
- **Crypto (ADR-0005):** `LinkTokens` (32-byte base64url 43-char token, SHA-256 at rest) and
  `DownloadKeys` (8-char `[A-Za-z0-9]` CSPRNG key, `Argon2id(pepper || key)` via Isopoh m=19 MiB
  t=2 p=1, `download_key_version` pepper version, dummy-verify for unknown tokens). The key/URL are
  returned exactly once at creation and never logged or listed.
- **Abuse controls (FR-LINK-009):** `KeyThrottle` state machine (5 failures/link+IP/15 min, 0.5/1/2/4 s
  progressive delay, 15-min lockout → 429 `too_many_attempts`; 100/hour/link → soft-lock + warning
  log), persisted in `download_key_attempts`/`abuse_lockouts` (Postgres-backed). Unknown tokens burn
  on a dummy verifier; lockouts skip hashing.
- **Reservation concurrency:** `TryReserveAsync` (release-expired + atomic `successful + reserved <
  max` `UPDATE … RETURNING`) with completion converting reserved→successful and failure/expiry/revoke
  releasing; counts increment only on stream completion (Complete/FailGrant CTEs extended for
  `link_id`).
- **Config:** `LinksOptions` (public TTL default 15 d + `DownloadKeyPepper`/`DownloadKeyVersion`)
  wired into `ConfigurationValidator`; the authoritative TTL and `public_links_enabled` are read from
  `platform_settings` (admin override; `public_links_disabled` → **409** per task, catalog/story
  aligned).
- **Portal Angular:** link management on file detail (`LinkCreateDialog` shows the key once + copy,
  list, `LinkRevokeDialog` with reason, `LinkStatsDialog`), an anonymous `/d/{token}` landing page
  (`PublicLinksApiService` via `HttpBackend` bypasses the auth interceptor), `DownloadService`-style
  two-step redeem→stream, and Spanish/English strings.
- **Tests:** unit (`DownloadKeys`, `LinkTokens`, `KeyThrottle`, `LinkQueryParsing`, config pepper);
  integration `PublicLinkTests` (create/list/revoke/stats, single-use, wrong-key 404, expired/revoked/
  max-reached 404, 3-way reservation concurrency, completion-only counting, owner isolation,
  integration-file 404, `public_links_enabled=false` 409, soft-lock 429); Angular specs; Playwright
  `links.spec.ts` (create → key shown once → `/d/{token}` → key → download) + axe-core.

**Decision:** `public_links_disabled` is HTTP **409** (not 403) per the S8 task; error catalog and
US-020 aligned. Unknown grant state after revoke/failure now maps to 404 `grant_not_found`.

**Next session:** S9/M5 integration API (idempotency, content/availability/delete, quotas, §15 demo).

---

## 2026-10-28 — S7/M3 implemented: portal file management + download grants

**Delivered (code + tests green: backend 70 integration / 145 unit, Angular 25 unit, Playwright 7 E2E + axe)**

- **Portal file management (contract-first from OpenAPI):** `PATCH /v1/portal/files/{fileId}`
  (`updateMyFile`) — If-Match → 412 `stale_version`, owner-scoped 404, rename (`invalid_filename`),
  retag (user-scoped `citext` get-or-create + whole-set replacement via `ITagRepository`), and
  retention (finite-only, ≤365, ≥10-char note above default, `legal_hold_active`). `DELETE
  /v1/portal/files/{fileId}` (`deleteMyFile`) — logical delete (`pending_deletion` +
  `deletion_requested_by*` + `next_deletion_at`), revokes active links and recomputes `access_mode`
  via `IDownloadLinkRepository`; `legal_hold_active` blocks. Both write `file.updated` /
  `file.deletion_requested` audit + outbox in one transaction.
- **Owner download grant (ADR-0003/0005):** `POST /v1/portal/files/{fileId}/downloads` issues a
  256-bit single-use 120 s grant (`source=owner`); `GET /v1/download-grants/{grantToken}` consumes it
  atomically (`issued→redeemed` raw-SQL `UPDATE … WHERE state='issued' AND expires_at > now()`),
  re-checks availability, streams from `IObjectStore` with RFC 5987 `Content-Disposition` +
  `no-store`/`no-referrer`/`nosniff`, and counts **only on completion** (`redeemed→completed` +
  `files.successful_downloads++` + `download_events` in one CTE). `NoSuchKey` → 404
  `file_unavailable`; expired → 410 `grant_expired`; reuse → 409 `grant_consumed`. No presigned
  URLs; no fetch-to-Blob.
- `FileMetadataMapper` now returns the file's real tags; `MarkUpdated` bumps the file `version`
  when only tags change so the ETag/If-Match stay consistent. New Application services
  (`IFileManagementService`, `IDownloadGrantService`) + repositories (`ITagRepository`,
  `IDownloadGrantRepository`, `IDownloadLinkRepository`) with Dapper SQL under
  `Persistence/Sql/Download/*.sql`.
- **Portal Angular:** list row actions (download/edit/delete + small-screen overflow menu),
  detail management, reusable `FileEditDialog`/`FileDeleteDialog` (type-to-confirm when the file has
  downloads), a two-step `DownloadService` (grant → `window.location.assign`), and error mapping for
  `stale_version`/`legal_hold_active`/`retention_*`/`grant_*`. Fixed two latent auth bugs surfaced
  by E2E: `AuthService.initialize()` now awaits the runtime config and calls MSAL v5's explicit
  `instance.initialize()` (was throwing `uninitialized_public_client_application`); list
  search/filter reload directly instead of relying on a no-op same-URL navigation.
- **Tests:** `PortalFileManagementTests` + `DownloadGrantTests` (PostgreSQL 18 + LocalStack S3)
  cover rename/tags/retention, If-Match 412, legal-hold 409, owner isolation 404, logical delete +
  link revocation, and grant issue/consume/single-use/expiry/NoSuchKey/integration-404.
  `RetentionRulesTests`/`GrantTokensTests` (unit); Angular `file-edit-dialog`/`file-delete-dialog`/
  `download.service`/`problem` specs; Playwright `e2e/portal.spec.ts` with a **mocked Entra session**
  (seeded MSAL v5 `sessionStorage` cache) + mocked API, plus `@axe-core/playwright` on `/files` and
  `/files/:id` (0 violations).

**Decisions / notes**

- The Angular download/“Descargar” button renders with `[hidden]="!isDownloadable(file)"` instead of
  `@if` because Angular 20.3's `@if` inside the list `@for` failed to render the button content in
  the dev build (verified via `ng.getComponent`: the method returned `true`); the `[hidden]`
  form is a safe, behavior-equivalent workaround.
- Portal tags are **user-scoped** (`scope='user'`, `owner_user_id` = owner) per US-007 “in my scope”;
  the S6 tag filter already matches by name and stays correct.
- `S3ObjectStore.GetObjectAsync` now throws `ObjectMissingException` on `NoSuchKey` so the API layer
  maps it to `file_unavailable` without referencing the AWS SDK.

**Next**

- S8: public links (8-char key, admin 15 d TTL) and visitor downloads.

---

## 2026-10-28 — S6/M3 implemented: portal shell and files UX

**Delivered (backend + portal; tests green)**

- **Backend portal files (contract-first):** `GET /v1/portal/files` (`listMyFiles`) and
  `GET /v1/portal/files/{fileId}` (`getMyFile`) in `PortalFilesEndpoints`. The list supports
  partial `ILIKE` filename search (`q`), tag filter (`file_tags` join), status filter, sort
  (`created_at`/`-created_at`/`name`/`-name`/`downloads`/`-downloads`, default `-created_at`),
  expiry range (`expiringWithinDays`/`expiresBefore`/`expiresAfter`), and keyset cursor pagination
  (`created_at`/`name`/`downloads` + `id` tiebreaker). Both endpoints are **owner-scoped**: a
  foreign or integration-owned file returns a uniform 404 `file_not_found` (no existence oracle,
  ADR-0012); the list never returns integration files.
- `IFileRepository.ListOwnedAsync` (EF Core LINQ) + `FileQueryParsing` (`FileSort`,
  `FileListCursor`, sort/status mapping, opaque cursor codec) in `Files.Application`; a minimal
  `FileTag` entity + `file_tags` EF mapping for the tag filter. `FileMetadataMapper` reused for the
  response shape. `TimeProvider` used for `expiringWithinDays` (never `DateTime.UtcNow`).
- **Role naming aligned to ADR-0014:** `PortalRoles` now emits `user`/`admin`/`auditor` (was
  `Files.User`/`Files.Admin`/`Files.Auditor`); OpenAPI `Me.roles` enum, `AuthApiTests`, and the
  `frontend-design.md` route matrix + §3 updated. The portal reads roles from `GET /v1/me`, never
  from `idTokenClaims.roles`.
- **Portal (Angular, standalone + Bootstrap 5.3):** lazy MSAL sign-in (guard + interceptor +
  `/auth/callback`, `sessionStorage`, silent renew on 401), shell with user/role from `/v1/me` and
  the es/en language selector, file list (300 ms debounced search, status badges, sort,
  cursor-in-URL pagination, drag-and-drop empty state), read-only detail, and chunked upload
  orchestration (XHR with progress, 2 chunks in flight, per-chunk retry/backoff, `X-Chunk-SHA256`,
  resume via status, complete then poll until `available`/`quarantined`).
- Runtime config now actually served from `/assets/config.json` (`src/assets` added to the
  Angular asset list); config.json carries no secrets. Filenames are rendered only by interpolation.
- Tests: `FileListQueryParsingTests` (unit) and `PortalFilesApiTests` (integration, PostgreSQL 18:
  search/filter/sort/cursor/expiry/tag, owner isolation, foreign/integration 404) on the backend;
  Angular unit tests for the upload orchestrator (mocked XHR), the auth interceptor, error mapping,
  and status pipes (Karma `ChromeHeadlessNoSandbox`).

**Decisions / notes**

- The tag filter is implemented against the existing `file_tags` table but tagging is still not
  persisted (arrives with S7), so it returns empty today and is forward-compatible.
- The cursor is opaque base64url JSON carrying `(createdAt, name, downloads, id)` so a single
  codec serves all sort orders; the `id` tiebreaker uses native PostgreSQL `uuid` comparison.
- `Features__MalwareScanning=false` and the S4/S5 scan/commit worker are untouched; S4/S5 upload
  endpoints and the portal/worker regression stay green.

**Next**

- S7: portal management (rename/tags/retention, delete, owner download grant), E2E Playwright +
  accessibility.

---

## 2026-10-28 — S5/M2 implemented: ClamAV scan-before-commit worker

**Delivered (code + tests green: 148 tests — 104 unit, 1 contract, 43 integration)**

- `ClamAvScanner` + `ClamAvProtocol` (Files.Infrastructure/Storage): streams the staged file to
  clamd over TCP `INSTREAM` (length-prefixed chunks, zero terminator) and maps `stream: OK` →
  clean, `stream: <name> FOUND` → infected, error/timeout/socket → failed. Uses
  `ClamAV__ScanTimeoutSeconds` (default 900) and never logs the filename. Unit-tested against a
  fake TCP clamd (clean/infected/timeout/refused) and the pure parser (ClamAvProtocolTests).
- `ClamAvOptions` (`ClamAV__Host/Port/ScanTimeoutSeconds`) + `ConfigurationValidator` validation;
  `AddFilesStorage` registers `ClamAvScanner` only when `Features__MalwareScanning=true` **and**
  `ClamAV__Host` is present, else `NullMalwareScanner` (fail-closed, D-35).
- Extracted a shared `UploadCommitter` (`CommitAsync` single/multipart + `HeadObject`,
  `FinalizeAsync`, `QuarantineAsync`, `MarkScanFailedAsync` with exponential `ScanBackoff`) so the
  API (scanning-disabled path) and the worker (scanning-enabled path) share one retryable commit.
- `UploadService.CompleteAsync`: with scanning enabled it marks `processing` +
  `scan_status=pending` + `committing`, writes `file.upload.scan_pending` audit/outbox, and returns
  `processing` without scanning/committing (worker model, ADR-0003); the disabled path still
  commits immediately (S4 regression green).
- `ScanCommitService` (claim one file via Dapper `FOR UPDATE SKIP LOCKED` + a lease, then scan →
  commit/available, quarantine, or retry) and `UploadScanCommitJob` (BackgroundService +
  PeriodicTimer, one scan at a time — clamd default). Worker `Program` now hosts it.
- `V4__scan_commit_worker.sql`: `files.scan_attempts` + `files.scan_next_attempt_at` (claim lease +
  backoff retry) + a `failed` retry index; validated from an empty PostgreSQL 18 (`scripts/db-verify.sh`).
- Local `docker-compose.yml` gains a `clamav` service (image `clamav/clamav:1.4`, port 3310, the
  `deploy/clamav/clamd.conf` with the 5 GiB limits, signatures volume); `.env.example` documents
  `ClamAV__*` (dev defaults to scanning off).
- Tests: `ClamAvProtocolTests`/`ClamAvScannerTests`/`ScanBackoffTests` (unit) and `ScanCommitTests`
  (integration) with a real `ClamAvFixture` (Testcontainers) — clean → available + `HeadObject`,
  EICAR → quarantined + absent from S3, scanner failure → `processing`/`scan_status=failed` then a
  retry after the backoff window. The `clamav/clamav:1.4` image is amd64-only, so on Apple Silicon
  (no emulation) the fixture reports `Available=false` and the ClamAV tests skip (`[Trait
  Category=ClamAV]`, `FILES_CLAMAV_TESTS=0` opt-out); CI Linux runs them for real.

**Decisions / notes**

- Scan failures update only `files` (never `upload_sessions`), so the session `updated_at` keeps
  pointing at completion and the S4 cleanup still fails the file after `AbortIncompleteAfterHours`
  (24 h). The worker retries with exponential backoff (30 s base, 30 min cap).
- A clean-but-failed S3 commit is treated like a scanner failure: `scan_status=failed` + backoff;
  the retry re-scans (idempotent) and re-commits, bounded by the 24 h window.
- `ScanWorkItem`/`IScanWorkClaimer` use Dapper with `FOR UPDATE SKIP LOCKED` and a lease stamp so
  concurrent worker replicas never double-scan (no Quartz/advisory locks — ADR-0007 deferred).

**Next**

- S6 portal (Angular app, MSAL, file list/detail, upload orchestration).

---

**Delivered (code + tests green: 118 tests — 77 unit, 1 contract, 40 integration)**

- `IObjectStore` port + AWS SDK v4 `S3ObjectStore` (single `PutObject`, multipart, `HeadObject`,
  `GetObject`, abort). Region/`ServiceURL` are mutually exclusive in SDK v4, so LocalStack uses
  `AuthenticationRegion` + `ServiceURL` (no instance role; static key from `S3__*`).
- `IChunkStore` (`FileChunkStore`): `O_EXCL` + `0600` staging files, pwrite at offset + fsync,
  free-space via `DriveInfo`. `StagingCapacity`/`UploadGeometry`/`Checksums`/`FileNamePolicy`/
  `ContentTypePolicy` pure helpers.
- `UploadChunk` entity + EF config + `DbSet`; `IUploadSessionRepository`/`IUploadChunkRepository`
  (EF) and `IUploadChunkWriter` (Dapper idempotent insert + atomic `received_bytes` increment).
- `UploadService` (create/chunk/status/complete/abort) enforcing the staging reserve, the 3-session
  concurrency limit, chunk idempotency/conflict, whole-file verification, and a retryable S3 commit
  (staging retained on `upload_commit_failed`). Malware seam `IMalwareScanner` + fail-closed
  `NullMalwareScanner`; scanning disabled commits immediately (ClamAV arrives with S5).
- Portal + integration endpoints (`MapUploadEndpoints`), `IntegrationWritePolicy` (`file.write`),
  RFC 9457 `insufficient_permission` on 403 and `invalid_token` on 401 for the API-key scheme.
- `UploadCleanupService` + worker `UploadCleanupJob` (expire sessions, abort stale multipart,
  delete orphan staging).
- `ConfigurationValidator` extended for the `Storage__*` limits; `AWSSDK.S3 4.0.104.7` added.
- OpenAPI: `502 CommitFailed` on both `complete` endpoints; `413`/`507` on integration create.
- Tests: unit (geometry, reserve, filename, content-type, checksums) + integration against real
  PostgreSQL 18 + LocalStack S3 (single-put e2e, multipart e2e with `HeadObject`, idempotency/
  conflict, checksum mismatch, incomplete, integration-without-`file.write` → 403, foreign session →
  404, abort, concurrency limit, reserve breach, commit-retry without re-upload, and cleanup
  expiry/orphan removal).

**Decisions / notes**

- "Max concurrent sessions" is host-wide and returns `507 staging_capacity_exceeded` (no dedicated
  code exists in the catalog); the disk reserve also returns `507`.
- `file_audit_events.file_id` FK means the file row must be flushed before the file-audit write.
- SDK v4 defaults auto CRC checksums; `RequestChecksumCalculation`/`ResponseChecksumValidation`
  are set to `WHEN_REQUIRED` so LocalStack multipart works (5 MiB minimum part size still enforced).
- Portal `tags` are accepted but not persisted (tagging arrives with S6).

**Next**

- S5: ClamAV scan/commit worker; then S6 portal.

---

## 2026-10-27 — S3b implemented: roles in Files + Files-issued API keys

**Delivered (code, tests green)**

- Roles from `users.role` (`UserRole` enum; JIT creates `user`; first admin bootstrapped by SQL).
  `PortalRoleRequirementHandler` is now scoped and reads the actor via `IIdentityService`; the token
  `roles` claim is ignored. Admin endpoints: `GET /v1/admin/users`, `PATCH /v1/admin/users/{id}/role`
  (last-admin guard + audit + outbox).
- API keys: `IntegrationApiKey` entity + `integration_api_keys` mapping; `ApiKeys` (prefix + 256-bit
  secret, SHA-256 hash, constant-time verify); `ApiKeyAuthenticationHandler` (`X-Api-Key`) enforcing
  integration status, expiry, revocation, IP allowlist, and scope intersection; admin endpoints
  `GET/POST /v1/admin/integrations/{id}/api-keys` and `DELETE .../api-keys/{keyId}` (secret shown
  once); `GET /v1/integrations/me`.
- Removed `BearerIntegration` (Entra client credentials) and `IntegrationRequirementHandler`'s `azp`
  lookup (now key scope claims); dropped `entraClientId` from admin DTOs and the repository.
- OpenAPI: `apiKey` header scheme; removed `entraClientId`, `allowAllTenant`/ACL/directory/private-link
  redemption; added users/API-key/integration-me endpoints; Redocly-valid.
- Tests: 56 passing (roles-from-DB, API-key issue/use/revoke, scope subset, last-admin guard).

**Remaining (next)**

- Align `docs/01-product/requirements.md` (FR-M2M-*, FR-ID-010/011, FR-LINK-*), user stories
  (US-010/011/012/020/021/022), the RBAC matrix, and the threat model to drop Entra app roles,
  Graph, and private links; refresh `docs/05-security/entra-id-authentication.md` and
  `machine-identity-and-authorization.md`; update `AGENTS.md` §6; run `make validate`.

---

## 2026-10-27 — Identity model realigned: Entra authenticates humans only (ADR-0014)

**Owner decision.** Entra ID is used **only** to sign in tenant employees (their accounts live in
Entra). Entra is not used for machine identity, directory data, or application roles. Integrations
authenticate with **Files-issued API keys**; private links (and Graph) are **out of scope for v1**.

**Recorded**

- New [ADR-0014](../02-architecture/adr/ADR-0014-entra-human-auth-only.md); ADR-0004 superseded;
  ADR-0005 amended (private links deferred).
- Decisions **D-41** (roles in Files), **D-42** (Files-issued API keys), **D-43** (no private links);
  D-20 and D-37 superseded.
- OQ-04, OQ-11, OQ-21 re-resolved; `AGENTS.md` canonical facts and guardrails; roadmap (new
  session **S3b**, M4/M5 scope, dependency graph); backlog (US-011/US-012/US-021, T-05, new
  T-17/T-18); data model.
- Migration **`V3`**: `users.role` (`user`/`admin`/`auditor`) and `integration_api_keys`
  (prefix + SHA-256 hash + scopes + expiry + revocation); `integrations.entra_client_id`
  deprecated.

**Not yet implemented (S3b) — the repo is temporarily inconsistent**

- The API still authorizes from the token `roles` claim and still accepts `BearerIntegration`
  (Entra client credentials). S3b must:
  - resolve roles from `users.role` (scoped authorization handler), JIT-create as `user`, and
    bootstrap the first admin by SQL;
  - add API-key authentication (prefix lookup, constant-time hash verify, scopes, expiry,
    revocation, IP allowlist) and admin endpoints (create/list/rotate/revoke, secret shown once);
  - remove Entra app roles and client credentials from the auth path;
  - remove private-link ACL/directory endpoints and DTOs from the OpenAPI contract and add the
    API-key endpoints.
- Until S3b lands, docs describe ADR-0014 while code/tests still assert the old model; do not
  deploy this state as final.

**Validated**

- `V3` applies from an empty PostgreSQL 18 (`scripts/db-verify.sh`).
- `scripts/check_docs.py` (links, IDs, ADR references).

**Next**

- S3b (identity realignment), then S4/M2 (storage and chunk upload).

---

## 2026-10-25 — Align to pre-existing infrastructure (remove Terraform, static S3 key)

**Scope:** the infrastructure already exists — one Ubuntu 24.04 host (public IP, SSH, ≥8 GiB) and
one S3 bucket reachable only from that host; there is no dev/staging, so development/testing run
entirely locally. Terraform no longer provisions anything.

**Changes**

- Removed `infra/terraform/` (EC2/EIP/SG/IAM/bucket/policy). Kept `infra/local/` (LocalStack init +
  local OTel).
- Added `deploy/host-bootstrap.sh` (idempotent: Docker + Compose, AWS CLI, directories, swap; does
  **not** touch the externally managed firewall).
- S3 auth switched from "EC2 instance role (no static keys)" to a **static access key** in
  `/opt/files/.env`; `deploy/env.prod.example` now carries `S3__AccessKeyId`/`S3__SecretAccessKey`,
  and `backup.sh`/`restore.sh` export them for the AWS CLI.
- Bucket policy/lifecycle are managed outside this repo and must not be edited by Files.
- Removed Terraform from `Makefile` (`fmt-tf`, `tf-validate`) and `.github/workflows/ci.yml`.
- Rewrote `deploy/README.md` and `docs/08-operations/infrastructure-and-iac.md` for the existing
  host; updated ADR-0010 (amended), ADR-0003, D-13/D-14/D-30/D-32, and the architecture/threat/
  upload/runbook/testing/cost/requirements docs, plus `AGENTS.md`, `README.md`, roadmap §S13, and
  the backlog/traceability.

**Decisions recorded**

- One tenant per deployment remains (D-01 Accepted). Production-only; dev/test 100 % local.
- Domain `files.sacservices.cloud` → host public IP (infra team creates the `A` record).
- Firewall/SSH already configured and left untouched.

**Still needed (data, not decisions)**

- S3 bucket name and `S3__AccessKeyId`/`S3__SecretAccessKey`.
- `Entra__TenantId`/`Entra__ApiClientId`/`Entra__PortalClientId`; Graph consent confirmation.
- SMTP app password (delivered out of band).

**Next**

- Unchanged: S4/S5 (M2). None of the outstanding data blocks M2.

---

## 2026-10-24 — Operational inputs confirmed (deployment unblockers)

**Scope:** answer the remaining operational questions for S13–S14. No product decision changed;
D-01 (one Entra tenant per deployment) moved from Provisional to **Accepted**.

**Confirmed by the owner**

- AWS: existing account, `us-east-1`; the **S3 bucket already exists and is reused**; the host is
  administered over **SSH via the public IP**.
- Entra: tenant and app registrations exist; the three Graph ACL permissions are approved with
  admin consent.
- Email: SMTP AUTH is enabled on `notificationsagent@ecs-la.com`; the app password is delivered out
  of band.
- DNS: the infra team creates the `A` record once the Elastic IP is known.
- Environments: **production only** (no staging). Budget approved. Manual deployment per
  `deploy/README.md`.

**Still needed (data, not decisions)**

- S3 bucket name; confirm its policy grants only the instance role. Terraform declares
  `aws_s3_bucket.this`, so S13 must `import` the existing bucket or switch to a data source.
- `Entra__TenantId`, `Entra__ApiClientId`, `Entra__PortalClientId`; confirmation that Graph admin
  consent landed (needed for M4).
- Admin CIDR and EC2 key-pair name for `admin_cidr`/`ssh_key_name`.
- SMTP app password in `/opt/files/.env`.

**Resolved conflict**

- Launch gate 9 assumed staging. The owner chose to relax it: the exact production Flyway sequence
  and rollback are rehearsed on a disposable instance/local PostgreSQL 18. Recorded in
  `roadmap.md` §4 and `open-questions.md`.

**Next**

- Unchanged: S4/S5 (M2). None of the outstanding data blocks M2.

---

## 2026-10-24 — S3/M1: Entra auth, JIT identity, and integration admin base

**Scope:** S3 (M1) — Entra ID JWT validation for humans and machines, JIT user provisioning, the
token-backed `ICurrentActor`, portal/integration authorization policies, contract-first `GET
/v1/me` and admin integration CRUD (no API keys), and admin audit + outbox.

**Delivered**

- `Files.Api/Authentication`: two JWT bearer schemes — `BearerUser` (delegated, rejects app-only)
  and `BearerIntegration` (app-only, requires the `Files.Integration` role) — sharing one
  `TokenValidationParameters` (signature RS256, `iss`, `aud` = `api://…` + client-id GUID, `exp`/
  `nbf` ≤2 min skew, `tid == Entra__TenantId`, `MapInboundClaims = false`). Challenges/forbids write
  RFC 9457 bodies (`invalid_token`/`token_expired`/`tenant_not_allowed`/`forbidden`). `JwtBearer`
  only — `Microsoft.Identity.Web` was not used (no token acquisition, no secrets).
- `Files.Api/Authorization`: `PortalRoleRequirement`(+handler) mapping roles → `User`/`Admin`/
  `Auditor` policies (admin implies user), and `IntegrationRequirement`(+handler) enforcing role +
  active integration via `azp`/`appid` + permissions read from `integration_permissions`.
- `Files.Api/Identity`: `IdentityService` (resolves `ResolvedActor` once per request),
  `TokenCurrentActor` (replaces `SystemCurrentActor`; System actor when unauthenticated), and
  `CurrentActorMiddleware`. `Files.Infrastructure/Identity/UserProvisioner` does JIT provisioning
  (`(tenant_id, object_id)` key; `last_login_at` updated ≤1/min; email/display name each login).
- Admin endpoints (`/v1/admin/integrations` CRUD + permissions) and `GET /v1/me`, contract-first
  from the OpenAPI: each admin mutation writes `admin_audit_events` (via the new
  `IAuditWriter.WriteAdminAuditAsync` + `InsertAdminAuditEvent.sql`) and an `outbox_messages` row
  in the same transaction. CORS reads `Cors__AllowedOrigins`.
- `integration_permissions` is now EF-mapped as part of the integration aggregate
  (`IntegrationPermissionEntry`; 13 entity types), so permissions persist atomically with the
  integration and are read from the DB, never from tokens.

**Validated**

- `dotnet build -warnaserror` (0 warnings), `dotnet test` (56 tests: 29 unit + 1 contract + 26
  integration), `dotnet format --verify-no-changes`.
- Auth integration tests mint self-signed RS256 JWTs (new `Entra__IssuerSigningKey` test-only key)
  and hit the real host against Testcontainers PostgreSQL 18 + Flyway: JIT, roles→policy, 401/403/
  404, app-only rejected on `/me`, admin create/disable/permissions with audit+outbox, and the
  `IntegrationRequirementHandler` denial matrix (missing role, unregistered `azp`, disabled, missing
  permission) reading permissions from the DB.

**Decisions / notes**

- `Entra__IssuerSigningKey` (base64 RSA public key) is a test/dev-only override for authority
  discovery; never set in production (documented in `EntraOptions`).
- Authorization requirements/handlers live in `Files.Api` (not `Files.Application`) so the
  Application project stays free of ASP.NET Core types beyond `Microsoft.Extensions.*`.
- 401 codes `integration_disabled` and unregistered-`azp`→401, plus IP-allowlist enforcement, are
  integration-endpoint concerns deferred to S4/S9; S3 ships the `IntegrationRequirement` with 403
  semantics and the auth-level `invalid_token`/`tenant_not_allowed`/`token_expired` 401s.
- Integration list cursor keysets on `created_at` (id as a deterministic tiebreaker); integrations
  are created one per transaction so `now()` collisions are effectively impossible.

**Next**

- S4/M2: `IObjectStore` (S3/LocalStack) + `IChunkStore` staging, upload-session/chunk endpoints,
  disk reserve and cleanup.

---

## 2026-10-24 — S2/M1: database and persistence base

**Scope:** S2 (M1) — EF Core mapping of the Flyway schema, domain entities/enums/value objects,
repository seams + EF implementations, Dapper transactional audit/outbox writers, and DB tests
against a real PostgreSQL 18 (Testcontainers + Flyway from an empty database).

**Delivered**

- `Files.Domain`: the 12 aggregate entities (`User`, `Integration`, `FileEntity`, `UploadSession`,
  `DownloadLink`, `DownloadLinkAcl`, `DownloadGrant`, `Tag`, `RetentionPolicy`,
  `NotificationPolicy`, `NotificationEvent`, `AuditEvent`) mapped 1:1 to the Flyway V1 tables, plus
  22 stable enums and the `Retention` value object (finite ⇔ expiry, 1..365-day window).
- `Files.Application`: ports `IUnitOfWork`, `ICurrentActor`, `IOutboxWriter`, `IAuditWriter`, and
  authorization-aware repository interfaces (`IUserRepository.GetByTenantIdentityAsync`,
  `IIntegrationRepository.GetByEntraClientIdAsync`/`GetBySlugAsync`,
  `IFileRepository.GetOwnedFileAsync`/`GetOwnedIntegrationFileAsync` — no bare `GetById`).
- `Files.Infrastructure`: `FilesDbContext` (Npgsql + snake_case naming; schema owned by Flyway, no
  EF migrations), one `IEntityTypeConfiguration` per entity, `FilesUnitOfWork` (single `SaveChanges`
  + transaction), `UserRepository`/`IntegrationRepository`/`FileRepository`, Dapper
  `OutboxWriter`/`AuditWriter` (SQL in `Persistence/Sql/*.sql` embedded resources), and
  `AddFilesPersistence` wired into the API (`ConnectionStrings__FilesDb`; no auto-migration on
  startup).
- Tests: domain-rule unit tests (`Retention`, `FileEntity` factories) and integration tests with
  Testcontainers PostgreSQL 18 + a Flyway `migrate` container from an empty DB — schema drift
  (every mapped table/column exists in `information_schema`), user/integration persistence, owner
  predicates (foreign file ⇒ null/404), and outbox + audit + entity sharing one transaction
  (commit and rollback paths).

**Validated**

- `dotnet build -warnaserror` (0 warnings), `dotnet test` (27 tests: 12 unit + 1 contract + 14
  integration), `dotnet format --verify-no-changes`.
- Integration tests provision PostgreSQL 18.6 and apply Flyway V1/V2 from an empty database. The
  `cidr[]` column maps via `System.Net.IPNetwork`; `S3UploadId`/`S3PartSizeBytes` map explicitly to
  `s3_upload_id`/`s3_part_size_bytes` (the naming convention merges the "S3" acronym).

**Decisions / notes**

- IDs are application-generated `Guid.CreateVersion7()` (`ValueGeneratedNever`); the Dapper writers
  rely on the DB `uuidv7()`/`now()` defaults.
- `version` is a concurrency token bumped in `FilesDbContext.SaveChangesAsync`; only
  `users`/`integrations`/`files` carry it in V1 (the other aggregates have no `version` column).
- The DbContext maps the 12 aggregate tables; operational tables (`outbox_messages`,
  `admin_audit_events`, `idempotency_records`, usage, …) stay Dapper-only for now and get read
  models in later sessions.
- Root-owned NuGet caches on this machine (`~/.nuget/packages`, `~/.local/share/NuGet/http-cache`)
  forced temporary caches: `NUGET_PACKAGES=/tmp/files-nuget/packages`,
  `NUGET_HTTP_CACHE_PATH=/tmp/files-nuget/http` (also required when running `make validate`).

**Next**

- S3/M1: Entra JWT validation, JIT user provisioning, role policies, integration admin (Entra app
  binding; no API keys), and the token-backed `ICurrentActor`.

---

## 2026-10-24 — S1/M0: executable solution skeleton

**Scope:** S1 (M0) — buildable solution, health/config endpoints, fail-fast configuration validation,
RFC 9457 problem details + correlation ID, structured logging, OpenTelemetry to the local collector,
API/worker Dockerfiles, and a complete local Docker Compose stack.

**Delivered**

- `.NET 10` solution `Files.slnx`: `Files.Domain`, `Files.Application`, `Files.Infrastructure`,
  `Files.Api`, `Files.Worker`, `Files.UnitTests`, `Files.IntegrationTests`, `Files.ContractTests`;
  `Directory.Build.props` (net10.0, nullable, warnings-as-errors, analyzers), `global.json`
  (SDK 10.0.x), and `tests/Directory.Build.props` (relaxes CA1707 for readable test names).
- `Files.Api`: `/health/live` (liveness) and `/health/ready` (real PostgreSQL probe), `/v1/config`
  (public non-sensitive config), RFC 9457 `ProblemDetails` with stable `code` + `traceId`,
  `X-Correlation-Id` middleware, Serilog structured JSON + OTLP logs, OpenTelemetry traces/metrics,
  and a fail-fast `ConfigurationValidator` (reports key names only, never values).
- `Files.Worker`: generic host with OpenTelemetry runtime metrics + Serilog/OTLP; placeholder
  `BackgroundService` (Quartz scheduler arrives in S2+).
- Angular 20 workspace in `web/` (standalone): Bootstrap 5.3, MSAL prepared (lazy-loaded
  `PublicClientApplication` + `/assets/config.json`), Spanish/English i18n with `@angular/localize`
  `$localize` + runtime `loadTranslations`, and a language selector.
- Dockerfiles (`src/Files.Api/Dockerfile`, `src/Files.Worker/Dockerfile`, `web/Dockerfile`) —
  multi-stage, non-root (`app` user), API liveness healthcheck — plus a root `.dockerignore`.
- `docker-compose.yml` now starts API, worker, and web alongside PostgreSQL 18, LocalStack S3,
  Mailpit, and the OTel collector (shared staging volume).

**Validated**

- `dotnet build -warnaserror` (0 warnings), `dotnet test` (11 tests), `dotnet format --verify-no-changes`.
- `npm ci && npm run build` in `web/` (MSAL lazy-loaded; initial bundle ~495 kB raw / ~99 kB transfer).
- `docker build` for the API, worker, and web images.
- Local stack: Flyway V1/V2 applied; API healthy (`/health/live` + `/health/ready`), `/v1/config`
  and 404 problem details with correlation ID; worker structured logs; web serving the SPA.

**Decisions / notes**

- Config keys are read with `:` separators (env `__` is translated to `:` by the config provider);
  the validator reports the operator-facing `__` names.
- MSAL is installed and prepared but inert without Entra app registrations (S3 wires the real IDs
  plus `MsalGuard`/`MsalInterceptor`).
- Bootstrap is imported as compiled CSS via `angular.json`; S6 moves to selective imports.
- `WebApplicationFactory` applies config overrides only after `CreateBuilder`, so integration tests
  satisfy the fail-fast validator via environment variables.
- Local dev on this machine: host port 8080 is occupied by an unrelated `open-webui` container, so
  the API was smoke-tested on 8081; the committed compose still maps 8080 (matches `AGENTS.md`).

**Next**

- S2/M1: EF Core mapping + repositories + audit/outbox; then S3/M1 Entra auth.

---

## 2026-10-23 — Product answers: retention, links, access, audit, UI

**Answers applied**

- Retention: **30 days default**, admin-configurable for new files; **365-day absolute maximum**;
  user may extend up to 365 with a **mandatory note**; no admin exceptions; indefinite only for
  integrations.
- Legal hold: **in v1**, reason required, blocks expiry/deletion, audited.
- Links: **public 15 d / private 30 d** server-defined TTLs (admin-configurable); owner cannot
  change. Private links use an ACL of Entra users/groups or `allow_all_tenant`.
- Access: admins **never** read content; one tenant per deployment; no folders, no preview, no
  download notifications.
- Machines: **Entra client credentials only**; API keys removed from schema/API/config/tests.
- Audit: **60 months** audit, **24 months** downloads; audit retention job updated.
- UI: **Spanish + English**, Spanish default; emails per recipient locale.
- DR: restore-based (RPO 24 h/RTO 4 h); single-node exit triggers: CPU/disk >80%, HA, RPO <24 h.
- Mailbox: `notificationsagent@ecs-la.com` via SMTP AUTH; password/app password pending in `.env`.

**Implementation changes**

- Migration `V1`: removed `integration_credentials`; added `download_link_acl`, link
  `allow_all_tenant`, file legal-hold metadata/retention note, 365-day checks, and ACL constraint.
- `V2`: retention policy 30/365; public/private link TTL settings 15/30.
- OpenAPI: removed credential paths/schemas; added directory search and legal-hold endpoints;
  link ACL/TTL fields; retention note; 47 paths / 59 operations.
- Requirements/stories/RBAC/ADRs/data model/security/testing/operations updated, including
  FR-LINK-016/017, FR-DEL-010, FR-GEN-007 and FR-M2M-004 rewrite.
- Email config uses `notificationsagent@ecs-la.com`; SMTP AUTH is the send path.

**Next**

- Implement S1–S5, then links/ACL and legal hold. Validate the new schema/contract as part of CI.

---

## 2026-10-23 — Domain, network, email, and malware answers (superseded by product answers)

**Answers received**

- Domain: **`files.sacservices.cloud`**, managed at **Network Solutions**; an `A` record will point
  to the Elastic IP when production deploys.
- Network: `us-east-1`, account **default VPC and public subnet** (Terraform creates the instance,
  not a custom network).
- Email: Microsoft 365 mailbox with **POP**; notifications will be sent through the same mailbox
  via **SMTP AUTH** (`smtp.office365.com:587`, STARTTLS), because POP is receive-only.
- Malware: **ClamAV** on the EC2, scanning the staged file **before** the S3 commit.

**Changes made**

- D-34…D-36, OQ-01/02/09/15/20/26 resolved; ADR-0009 rewritten for SMTP (Graph optional);
  ADR-0010/0003 updated for domain, `t3.large`, and scan-before-commit.
- Requirements FR-UP-015/018, user stories, lifecycle (T2/T3/T4), upload flow, OpenAPI descriptions,
  threat model (malware + SMTP), backend config (`Smtp__*`, `ClamAV__*`), observability/runbooks,
  cost model, roadmap/backlog, deploy assets, README/AGENTS/Spanish summary.
- Production Compose adds `clamav` (clamd 3310, signatures volume, 2 GiB limit) and the custom
  `deploy/clamav/clamd.conf` with `MaxFileSize`/`StreamMaxLength` raised for 5 GiB files.
- Terraform default instance is now `t3.large` (8 GiB) for ClamAV + PostgreSQL + API/worker;
  `us-east-1` remains the default; default VPC/subnet is used unless `vpc_id`/`subnet_id` are set.
- `deploy/env.prod.example` uses `files.sacservices.cloud`, M365 SMTP settings, and ClamAV on.

**Notes**

- ClamAV images are **amd64-only**; the production EC2 is x86_64. Local Apple Silicon development
  keeps scanning off (and cannot run the ClamAV image without emulation).
- The mailbox address/password and confirmation that SMTP AUTH is enabled are still required; if
  the tenant blocks SMTP AUTH, use the Graph `Mail.Send` fallback.
- `files.sacservices.cloud` must resolve to the Elastic IP before Caddy can obtain TLS.

---

## 2026-10-23 — Re-scope to one EC2 + exclusive S3 (partially superseded)

**Change requested:** the construction phase has a single EC2 instance (Ubuntu 24.04 LTS, public
IP) plus one S3 bucket; no ALB/ECS/RDS/ECR/CloudWatch. The bucket may be accessed **only** by
Files. The previous ECS/RDS/ALB and presigned-upload design is superseded for this phase.

**What changed**

- Hosting: one EC2 (`t3.medium` default, 100 GiB gp3 EBS, Elastic IP) running Docker Compose with
  Caddy, API, worker, and PostgreSQL 18. Caddy is the only public listener; SSH is restricted to
  `admin_cidr` (D-31/D-32, ADR-0010).
- Uploads: **API-proxied 16 MiB chunks** into a local EBS staging file, verified, then uploaded to
  S3 by the API using server-side multipart. No presigned URLs; clients never touch S3 (D-07/D-30,
  ADR-0003).
- Schema: `upload_sessions` now stores chunk geometry/staging path; `upload_parts` became
  `upload_chunks` (offset, size, hash). No S3 upload ID at session creation.
- OpenAPI: chunk `PUT`/status endpoints replace presign endpoints; proxy upload removed.
- S3: bucket policy denies object/list/multipart actions to every principal except the EC2 instance
  role; no CORS; API/worker use the instance role.
- Operations: Terraform rewritten (instance + storage modules), `deploy/` added (Compose, Caddy,
  env template, backup/restore/smoke), backups are nightly `pg_dump` to S3 (RPO 24 h/RTO 4 h),
  SLOs adjusted to 99.5%, cost model to ~US$80–90/month.
- Docs updated across requirements, stories, architecture, data model, API, security, workflows,
  operations, roadmap/backlog/traceability, AGENTS/README/Spanish summary.

**Validated**

- `V1`/`V2` applied on PostgreSQL 18.6 with the new upload tables; 27 tables; invariants pass.
- OpenAPI parses and passes Redocly; chunk endpoints/schemas present, no presign remnants.
- `terraform fmt`/`validate` succeed for the EC2 + S3 stack; no module cycles.
- Local stack still uses LocalStack 3.8 (no S3 CORS needed).

**Next**

S1 starts the application scaffold as before, but the first vertical slice must use chunk uploads +
staging + server-side S3 commit, and the production deploy follows `deploy/README.md`.

---

## 2026-10-23 — Design package v1 (pre-implementation)

> **Partially superseded:** hosting and upload decisions below were replaced by the re-scope
> entry above. Keep this entry as history.

**Goal:** Turn the source specification v0.2 into an implementation-ready package that future
sessions can execute without re-deriving decisions.

**Produced**

- Requirements with 121 FR IDs and 20 NFRs, 31 user stories with Gherkin acceptance criteria, RBAC
  matrix, decision log (D-01…D-28), 24 open questions with defaults.
- Architecture (C4 context/container/component, flows, deployment), backend and frontend designs,
  13 ADRs.
- PostgreSQL 18 data model, Flyway `V1` schema (27 tables) and `V2` reference data, invariants,
  concurrency SQL, retention schedule.
- OpenAPI 3.1 contract (47 paths, 58 operations), API conventions, error catalog.
- Entra ID design, machine identity/API keys, STRIDE threat model with key-entropy analysis.
- File lifecycle state machine, upload/download design, retention/notification/deletion workflows,
  and the existing-product file backfill/cutover plan.
- Testing strategy, SLOs/alerting, deployment/rollback, cost model, runbooks, infrastructure/IaC.
- Roadmap (M0–M7, S1–S14), backlog, traceability matrix.
- Local stack (PostgreSQL 18, LocalStack 3.8 S3, Mailpit, OTel), CI skeleton, validation scripts,
  Terraform skeleton for AWS.

**Validated**

- `V1` + `V2` applied to a fresh PostgreSQL **18.6** container; seeds present; invalid owner,
  retention, link, notification-duplicate, and tag-duplicate states rejected.
- Atomic link reservation (`max_downloads`) exercised: two reservations succeed, third fails.
- OpenAPI parses (3.1.0) and passes Redocly recommended rules (0 warnings after config).
- `docker compose config` valid.
- Local documentation links and ID references checked (`scripts/check_docs.py`).
- Terraform `fmt`/`validate` (AWS provider 6.70.0, random 3.9.1); `terraform graph` checked for
  module cycles after moving security groups into their own module.
- Local stack started and exercised: PostgreSQL 18.6, LocalStack 3.8.1 with a private
  `files-local` bucket and upload CORS, Mailpit, OTel Collector; Flyway 11.20.3 applied both
  migrations; CORS preflight for presigned `PUT` returned the expected headers.

**Decisions worth remembering**

- Downloads stream through the API; no presigned GET (ADR-0003/0005). Counts increment only on
  completion; link limits use reservations.
- 8-character key + 256-bit link token; Argon2id key verifier and versioned pepper; uniform 404s.
- Integration files: indefinite retention, integration-only, no human search visibility.
- Graph `sendMail` instead of POP (OQ-01), pending mailbox/app-permission confirmation.
- Terraform targets ECS Fargate + RDS PostgreSQL 18 + private S3 + ALB.

**Open blockers / watch items**

- OQ-01 email mailbox/permission; OQ-02 AWS account/region; OQ-07 retention defaults; OQ-15
  domain/certificate. None block S1.
- Application code is **not started**; the next session is S1 (M0).

**Next session**

Follow `AGENTS.md` §7: scaffold the .NET solution and Angular workspace, health endpoints, config
validation, OTel wiring, local stack, CI. Update the progress table in `roadmap.md` and append a
new entry here.
