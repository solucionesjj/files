# Roadmap and Delivery Plan

**Status date:** 2026-10-29 · **Phase:** Implementation — M7 hardening (S12 delivered; S13/S14 remain).
Progress is updated at the end of every work session (see [AGENTS.md](../../AGENTS.md)).

## 1. Milestones

| ID | Milestone | Outcome | Exit criteria |
|---|---|---|---|
| M0 | Foundations | Buildable repo, local stack, CI, health, telemetry | `docker compose up` works; API/worker build; health endpoints; CI green; `make validate` passes. |
| M1 | Identity, data, admin base | Schema, Entra auth, RBAC, integrations | Migrations applied; JIT user; `/me`; admin integration + API keys; roles in Files; audit rows; auth tests green. |
| M2 | Upload and storage | Chunked API upload, local staging, S3 commit, verification, cleanup | 5 GiB chunked upload works against LocalStack S3; staging/disk checks; stale sessions and multipart cleaned; tests. |
| M3 | Portal MVP | Human file management | Sign-in, upload, list/search/tags, detail, download, delete; accessibility scan; E2E. |
| M4 | Links and public access | Public links, keys, grants, abuse controls (private links deferred) | Key shown once; public grant flow; limits under concurrency; lockout; stats; tests. |
| M5 | Integration API | Machine consumers end-to-end | API keys only; idempotency; upload/read/content/delete/availability; quotas; §15 flow demo. |
| M6 | Retention, notifications, deletion | Lifecycle automation | Policy versions; legal hold; M365 SMTP email; unique notifications; expiry/deletion jobs; ClamAV scan/commit; reconcile; admin UI/API. |
| M7 | Hardening and launch | Production readiness | Pen test fixes; load/soak; DR drill; SLO dashboards/alerts; cost approved; runbooks; OQ blockers resolved. |

## 2. Session plan

A "session" is a focused work block (roughly 1–2 engineering days for one agent/person). Sessions
can run partly in parallel once their dependencies are met.

| Session | Milestone | Goal | Primary outputs |
|---|---|---|---|
| S1 | M0 | Bootstrap the solution | `Files.slnx`/`Files.sln`, projects, `Directory.Build.props`, `global.json`, Angular workspace, compose, `/health`, config validation, CI, `make validate`. |
| S2 | M1 | Database and persistence base | Flyway runner, EF DbContext + naming, repository seams, user/integration entities, audit writer, outbox writer, DB tests. |
| S3 | M1 | Entra auth and admin base | JWT validation, policies, JIT users, `/me`, integration CRUD (API keys), admin audit. |
| S3b | M1/M5 | Identity realignment (ADR-0014) | `users.role` roles in Files; API-key issuance/verification + admin endpoints; remove Entra app roles and client credentials; drop private links/Graph from scope; apply `V3`. |
| S4 | M2 | Storage adapter, staging, sessions | ✅ `IObjectStore` (S3; LocalStack locally), `IChunkStore` staging, create session, chunk endpoints, disk/quota checks, retryable commit (scanning disabled). |
| S5 | M2 | Completion, ClamAV scan/commit, cleanup | Verify chunks/size/checksum, scan before S3, server-side multipart/put, HeadObject, retry commit, abort/cleanup, integration tests. |
| S6 | M3 | Portal shell and files UX | Angular app, MSAL, API client, file list/search/filters/pagination, detail, upload orchestration. |
| S7 | M3 | Portal management | Rename/tags/retention, delete, owner download grant, E2E + accessibility, error UX. |
| S8 | M4 | Public links and downloads | Link create/list/revoke, admin TTL, public metadata, key verify, grants, reservation concurrency, abuse controls, stats (private links deferred). |
| S9 | M5 | Integration API | API-key auth handlers, idempotency filter, content/availability/delete, chunk upload, quotas, §15 demo, OpenAPI contract tests. |
| S10 | M6 | Retention, legal hold, notifications | Policy versioning (30/365 + note), legal hold, scheduler, templates/renderer, M365 SMTP sender, retries, admin endpoints/UI. |
| S11 | M6 | Expiration and deletion | Expiration/deletion jobs (skip legal hold), tombstones, reconciliation, retention purge, metrics/alerts. |
| S12 | M7 | Test hardening and security | IDOR/auth matrices, upload tampering, ZAP, dependency/IaC scans, pen-test fixes, load/soak. |
| S13 | M7 | Operations readiness | Host bootstrap + Docker Compose/Caddy deploy to the existing host, backup/restore drill, dashboards/alerts, runbooks rehearsal, cost review. |
| S14 | M7 | Launch | Staging sign-off, migration/backfill rehearsal, production cutover, post-launch monitoring. |

Dependency graph (critical path):

```
S1 → S2 → S3 → S3b → S4 → S5 → S9 → S11 → S12 → S13 → S14
                         ↘ S6 → S7 → S8 → S10 ↗
```

S3b (identity realignment) must land before S6/S9 so the portal and integrations use Files-managed
roles and API keys. S6/S7 (portal) can start once S4 exposes upload endpoints; S8 depends on S5 and
S6. S10 depends on S5 and S9. S11 depends on S10.

## 3. Progress

| Milestone | Design | Implementation | Notes |
|---|---|---|---|
| M0 | ✅ | ✅ | S1 done: .NET 10 solution, Angular workspace, health/config endpoints, OTel, Dockerfiles, local compose. |
| M1 | ✅ | ✅ | S1–S3 done: foundations, EF mapping + repositories, audit/outbox writers, Entra JWT auth (human-only per ADR-0014), JIT users, roles in Files, `/me`, integration admin CRUD, API keys, admin audit. |
| M2 | ✅ | ✅ | S4 done: `IObjectStore` (S3/LocalStack), staging, chunk endpoints, retryable server-side commit (scanning disabled). S5 done: ClamAV scan-before-commit worker (`ClamAvScanner`, `ScanCommitService`, `UploadScanCommitJob`), quarantines infected files, retries scanner failures with backoff, and keeps the scanning-disabled path + cleanup green. |
| M3 | ✅ | ✅ | S6 done: portal shell (MSAL sign-in + `/v1/me` roles), owner-scoped file list/search/filters/sort/cursor pagination, read-only detail, and chunked upload orchestration. S7 done: rename/tags/retention, logical delete, owner download grant (256-bit single-use 120 s API stream, counted on completion), If-Match 412 / legal-hold 409, E2E Playwright + axe accessibility. S8 (links) remains. |
| M4 | ✅ | ✅ | S8 done: public links and downloads — create/list/revoke/stats (owner-scoped), admin TTL (15 d), 8-char key shown once (Argon2id + versioned pepper), anonymous metadata + key redemption, 120 s single-use grants, atomic `max_downloads` reservation (concurrency), abuse controls (progressive delay/lockout/soft-lock + alert), completion-only counting, portal link management UI + `/d/{token}` landing page, Playwright E2E + axe-core. Private links/Graph deferred (ADR-0014). |
| M5 | ✅ | ✅ | S9 done: integration file endpoints (`GET /v1/integrations/files`, `GET/DELETE /v1/integrations/files/{fileId}`, `GET .../availability`, `GET .../content`) on top of the API-key auth + chunk-upload machinery; ownership OR active `integration_file_grants` (foreign → 404, grants read-only); `Idempotency-Key` (24 h, replay / `idempotency_key_reuse` 422 / `idempotency_in_progress` 409); per-integration quotas (max file size, concurrent sessions, daily bytes via `daily_usage` → 429 `quota_exceeded`); single-range downloads (`Range` 206/416) counted only on completion (source/actor = integration); §15 flow + contract/unit/integration tests. |
| M6 | ✅ | ✅ | S10 done: retention policy versioning (publish + history), legal hold (place/release, blocks expiry + delete), admin retention override, bulk retention migrations, notification policy + email-template versioning, notification scheduling/sending (SMTP via MailKit, dedupe/supersede/retry/attempt states), and the expiration job (skip legal hold). S11 done: physical S3 deletion job (idempotent, retry/backoff, legal-hold skip), tombstones + 60-month retention purge, DB↔S3 reconciliation (missing-object quarantine + orphan move), and lifecycle metrics/alerts (`files_expired_total`, `files_deletion_failures_total`, `reconcile_*`, `job_last_success` gauge). |
| M7 | ✅ | 🟡 | S12 done: authorization/IDOR matrix (`AuthorizationMatrixTests`), upload tampering (`UploadTamperingTests` + `ContentDispositionTests`), secret/log-leakage guard (`LogRedactionTests` + `check-security-static.sh`), gitleaks + Trivy + SAST wired into CI and `make` targets (see [security scanning](../07-quality/security-scanning.md)), ZAP baseline script + CSP, and k6 load/soak scenarios with a CI smoke gate. S13 done: ops readiness — `deploy/deploy.sh` + `deploy/rollback.sh` (backup → Flyway migrate → SPA build → compose up → smoke → REVISION), backup/restore + migration/rollback drills recorded (gates 5 & 9), SigNoz dashboards/alerts + on-call (gate 6), runbook rehearsal, cost review + budget thresholds (gate 7), and the read-only admin/audit surface (audit-events/download-events/usage/settings) implemented contract-first with the mutating admin ops deferred. S14 (launch) remains. |

Legend: ✅ complete · 🟡 in progress · ⬜ not started.

## 4. Launch gates (all required)

1. M0–M6 exit criteria met; M7 checks complete.
2. OQ-01 (email provider/mailbox), OQ-07 (retention defaults), OQ-15 (domain/cert), OQ-19/20
   (scope/budget) resolved.
3. Threat model reviewed; high-severity pen-test findings fixed; IDOR/auth suites green.
4. Load test at 2× baseline; 5 GiB upload/download verified; no memory growth.
5. Restore drill executed within RTO/RPO; results recorded.
6. SLO dashboards + alerts live; on-call rota and runbooks reviewed.
7. Cost estimate approved; budgets/alerts configured.
8. Legal wording for deletion/backups approved; privacy review done.
9. The exact production migration sequence (Flyway `V1`/`V2`) and the rollback have been rehearsed
   on a disposable instance or local PostgreSQL 18; results recorded.

> **Resolved (2026-10-25):** the owner approved **production-only** (no staging environment). Gate 9
> is met by that disposable-instance rehearsal, not a standing staging environment.
10. Existing-file backfill plan (if applicable) has a dry run, mapping, and dual-run/cutover plan.
11. Support/operations training for link revocation, credential rotation, and deletion failures.

> **S13/M7 status (2026-10-29):** gate 5 (restore drill — `deploy/drill-backup-restore.sh`, RTO
> 338 ms ≪ 4 h), gate 6 (dashboards/alerts + on-call — `deploy/observability/`), gate 7 (cost
> budget $115/mo + 80/100/120% alerts), and gate 9 (Flyway V1→V6 + rollback —
> `deploy/drill-migrations.sh`) are **executed and recorded** in
> [runbooks.md](../08-operations/runbooks.md#drill-log) and the [session log](session-log.md). Gates
> 1–4, 8, 10, 11 are unchanged from prior sessions.

## 5. Risks to the plan

| Risk | Impact | Mitigation |
|---|---|---|
| Email provider not approved (OQ-01) | Blocks M6 exit | Decide early; the `IEmailSender` seam allows SMTP/SES fallback. |
| Entra app registrations/consent delays | Blocks M1/M5 | Start the IT request in S1; use mock OIDC until available. |
| 5 GiB performance/disk issues | M2/M7 slip | Test early with LocalStack S3 and a real host disk; tune chunk/S3 part sizes and staging reserve. |
| Single-node outage | Launch risk | Document 99.5%/RPO 24h, rehearse restore, keep the scale-up path ready. |
| Scope creep (preview, folders, sharing) | Delay | Explicitly out of scope; change control. |
| Single-tenant assumption wrong | Architecture rework | Confirm OQ-03 before M1 schema is frozen (it is already designed with no tenant columns). |
| AWS quota/limits | Deployment delay | Confirm S3 bucket access and the SSH key early; no instance/EIP provisioning is needed (host pre-exists). |
| Key brute force incident | Security | Throttling + monitoring designed; rehearse runbook. |
| Backup erasure expectations | Legal | ADR-0008 wording; legal sign-off. |

## 6. Next session checklist

1. Read `AGENTS.md`, this roadmap, and the [session log](session-log.md).
2. ✅ **S10 (done):** retention, legal hold, and notifications — policy versioning (30/365 + note), legal hold, admin retention override, bulk retention migrations, notification policy + email-template versioning, notification scheduler/sender (SMTP via MailKit, dedupe/supersede/retry/attempt states), expiration job (skip legal hold), and contract/unit/integration tests (incl. Mailpit SMTP sink).
3. ✅ **S11 (done):** expiration and deletion completion — physical S3 deletion job (idempotent, retry/backoff, legal-hold skip), tombstones + 60-month retention purge, DB↔S3 reconciliation (missing-object quarantine + orphan move), lifecycle metrics/alerts, and unit/integration tests (PostgreSQL 18 + LocalStack S3).
4. **S12 (done):** test hardening and security — IDOR/auth matrix, upload tampering, secret/dependency/image scanning, ZAP baseline + CSP, and k6 load/soak (smoke in CI). See the [session log](session-log.md) and [security scanning](../07-quality/security-scanning.md).
5. ✅ **S13 (done):** operations readiness — `deploy/deploy.sh` + `deploy/rollback.sh` (backup → Flyway migrate → SPA build → compose up → smoke → REVISION), backup/restore + migration/rollback drills on disposable PostgreSQL 18 (gates 5 & 9, results in [runbooks.md](../08-operations/runbooks.md#drill-log)), SigNoz dashboards/alerts + on-call (gate 6), runbook rehearsal + fixes, cost review + budget thresholds (gate 7), and the read-only admin/audit surface (audit-events/download-events/usage/settings) implemented contract-first with mutating admin ops deferred (see [session log](session-log.md)).
6. **S14 (next):** launch — staging sign-off, production cutover, post-launch monitoring.
