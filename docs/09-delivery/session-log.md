# Session Log

Append one entry per work session. Keep it short: what changed, what was validated, what is next,
and any decision that future sessions must know. Do not rewrite history; correct it with a new
entry.

---

## 2026-10-24 — S1/M0: executable solution skeleton (current)

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
