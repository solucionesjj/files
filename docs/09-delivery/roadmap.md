# Roadmap and Delivery Plan

**Status date:** 2026-10-23 · **Phase:** Design complete; implementation not started.
Progress is updated at the end of every work session (see [AGENTS.md](../../AGENTS.md)).

## 1. Milestones

| ID | Milestone | Outcome | Exit criteria |
|---|---|---|---|
| M0 | Foundations | Buildable repo, local stack, CI, health, telemetry | `docker compose up` works; API/worker build; health endpoints; CI green; `make validate` passes. |
| M1 | Identity, data, admin base | Schema, Entra auth, RBAC, integrations/credentials | Migrations applied; JIT user; `/me`; admin integration + key issuance; audit rows; auth tests green. |
| M2 | Upload and storage | Presigned multipart, verification, cleanup | 5 GiB multipart works against LocalStack S3; size/type/checksum enforcement; stale upload aborted; tests. |
| M3 | Portal MVP | Human file management | Sign-in, upload, list/search/tags, detail, download, delete; accessibility scan; E2E. |
| M4 | Links and public access | Public/private links, keys, grants, abuse controls | Key shown once; grant flow; limits under concurrency; lockout; stats; tests. |
| M5 | Integration API | Machine consumers end-to-end | Client credentials + API key; idempotency; upload/read/content/delete/availability; quotas; §15 flow demo. |
| M6 | Retention, notifications, deletion | Lifecycle automation | Policy versions; email via Graph (or approved provider); unique notifications; expiry/deletion jobs; reconcile; admin UI/API. |
| M7 | Hardening and launch | Production readiness | Pen test fixes; load/soak; DR drill; SLO dashboards/alerts; cost approved; runbooks; OQ blockers resolved. |

## 2. Session plan

A "session" is a focused work block (roughly 1–2 engineering days for one agent/person). Sessions
can run partly in parallel once their dependencies are met.

| Session | Milestone | Goal | Primary outputs |
|---|---|---|---|
| S1 | M0 | Bootstrap the solution | `Files.slnx`, projects, `Directory.Build.props`, `global.json`, Angular workspace, compose, `/health`, config validation, CI, `make validate`. |
| S2 | M1 | Database and persistence base | Flyway runner, EF DbContext + naming, repository seams, user/integration entities, audit writer, outbox writer, DB tests. |
| S3 | M1 | Entra auth and admin base | JWT validation, policies, JIT users, `/me`, integration CRUD, API key issue/revoke, admin audit. |
| S4 | M2 | Storage adapter and upload sessions | `IObjectStore` (S3; LocalStack locally), create session, multipart initiate, presign, upload session state, quota checks. |
| S5 | M2 | Completion and cleanup | Complete/verify (ListParts/HeadObject/checksum), single PUT, proxy staging, abort/cleanup job, integration tests. |
| S6 | M3 | Portal shell and files UX | Angular app, MSAL, API client, file list/search/filters/pagination, detail, upload orchestration. |
| S7 | M3 | Portal management | Rename/tags/retention, delete, owner download grant, E2E + accessibility, error UX. |
| S8 | M4 | Links and public downloads | Link create/list/revoke, public/private metadata, key verify, grants, reservation concurrency, abuse controls, stats. |
| S9 | M5 | Integration API | Integration auth handlers, idempotency filter, content/availability/delete, proxy upload, quotas, §15 demo, OpenAPI contract tests. |
| S10 | M6 | Retention and notifications | Policy versioning, scheduler, templates/renderer, Graph sender, retries, admin endpoints/UI. |
| S11 | M6 | Expiration and deletion | Expiration/deletion jobs, tombstones, reconciliation, retention purge, metrics/alerts. |
| S12 | M7 | Test hardening and security | IDOR/auth matrices, upload tampering, ZAP, dependency/IaC scans, pen-test fixes, load/soak. |
| S13 | M7 | Operations readiness | Terraform production plan, dashboards, alerts, DR drill, runbooks rehearsal, cost review. |
| S14 | M7 | Launch | Staging sign-off, migration rehearsal, production cutover, post-launch monitoring. |

Dependency graph (critical path):

```
S1 → S2 → S3 → S4 → S5 → S9 → S11 → S12 → S13 → S14
                 ↘ S6 → S7 → S8 → S10 ↗
```

S6/S7 (portal) can start once S4 exposes upload endpoints; S8 depends on S5 and S6. S10 depends on
S5 and S9. S11 depends on S10.

## 3. Progress

| Milestone | Design | Implementation | Notes |
|---|---|---|---|
| M0 | ✅ | ⬜ | Design docs complete; infra skeleton present; app scaffold not started. |
| M1 | ✅ | ⬜ | Schema validated on PostgreSQL 18. |
| M2 | ✅ | ⬜ | Upload/download design finalized. |
| M3 | ✅ | ⬜ | Frontend design finalized. |
| M4 | ✅ | ⬜ | Link/key/grant design finalized. |
| M5 | ✅ | ⬜ | OpenAPI contract and integration flow documented. |
| M6 | ✅ | ⬜ | Background jobs and notifications designed. |
| M7 | ✅ | ⬜ | SLOs, runbooks, cost model, threat model ready. |

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
9. Staging has run the exact production migration sequence; rollback rehearsed.
10. Support/operations training for link revocation, credential rotation, and deletion failures.

## 5. Risks to the plan

| Risk | Impact | Mitigation |
|---|---|---|
| Email provider not approved (OQ-01) | Blocks M6 exit | Decide early; the `IEmailSender` seam allows SMTP/SES fallback. |
| Entra app registrations/consent delays | Blocks M1/M5 | Start the IT request in S1; use mock OIDC until available. |
| 5 GiB performance issues | M2/M7 slip | Test early with LocalStack S3 and real S3; part-size tuning. |
| Scope creep (preview, folders, sharing) | Delay | Explicitly out of scope; change control. |
| Single-tenant assumption wrong | Architecture rework | Confirm OQ-03 before M1 schema is frozen (it is already designed with no tenant columns). |
| Terraform/AWS quota limits | Deployment delay | Request quotas (Fargate, RDS, EIP) in S1. |
| Key brute force incident | Security | Throttling + monitoring designed; rehearse runbook. |
| Backup erasure expectations | Legal | ADR-0008 wording; legal sign-off. |

## 6. Next session (S1) checklist

1. Read `AGENTS.md` and this roadmap.
2. Create the .NET solution/projects and Angular workspace (`scripts/bootstrap-solution.sh`).
3. Wire configuration + options validation, Serilog/OTel, `/health/live` and `/health/ready`.
4. Bring up compose dependencies and apply Flyway migrations.
5. Add CI (build, unit, integration skeleton, OpenAPI lint, Terraform fmt).
6. Update the progress table and add a short entry to `docs/09-delivery/roadmap.md` (or a
   `CHANGELOG`-style session log) with what actually happened.
