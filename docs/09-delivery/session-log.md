# Session Log

Append one entry per work session. Keep it short: what changed, what was validated, what is next,
and any decision that future sessions must know. Do not rewrite history; correct it with a new
entry.

---

## 2026-10-23 — Re-scope to one EC2 + exclusive S3 (current)

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
