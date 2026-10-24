# AGENTS.md — Working Agreement for Future Sessions

**Read this first.** This repository is the delivery package for **Files**, a secure file storage,
transfer, and integration platform. The design is complete; implementation starts at milestone M0.

**Status:** 2026-10-24 — documentation, data model, API contract, operations, and delivery plan
complete. **S1–S3/M1 implemented** (.NET 10 solution, Angular workspace, health/config endpoints,
OpenTelemetry, Dockerfiles, local compose, EF Core persistence + repositories + audit/outbox
writers, and Entra JWT authentication with JIT users, role policies, and integration admin).
Terraform skeleton under `infra/terraform/`.

---

## 1. What Files is

- **Portal** (Angular + Bootstrap) for tenant employees to upload, organize, retain, and share
  files.
- **API** (.NET 10) for machine integrations to upload/retrieve files without a user session.
- **Private S3** storage, reachable only by Files, with chunked API uploads and streamed
  downloads; PostgreSQL 18, Caddy, and ClamAV run in Docker on **one EC2 instance**; Entra ID
  identity, Microsoft 365 SMTP email, SigNoz telemetry.
- Public links require an **8-character download key** in addition to a 256-bit link token.
- One Entra tenant and one AWS account per environment (v1).

Source specification: `FILES_Product_Requirements_Technical_Specifications.md` (v0.2).

## 2. Repository map

| Path | Contents |
|---|---|
| `docs/00-resumen-ejecutivo.md` | Spanish executive summary (orientation for stakeholders). |
| `docs/01-product/` | Requirements (FR/NFR IDs), user stories, RBAC, decisions, open questions, glossary. |
| `docs/02-architecture/` | C4 architecture, backend/frontend design, 13 ADRs. |
| `docs/03-data/` | Data model, invariants, retention schedule, EF notes. |
| `db/migrations/` | **Canonical DDL** (Flyway `V1`, `V2`). |
| `docs/04-api/` | Conventions, error catalog, OpenAPI 3.1 contract. |
| `docs/05-security/` | Entra ID design, machine identity, threat model. |
| `docs/06-workflows/` | Lifecycle state machine, upload/download, retention/notifications/deletion. |
| `docs/07-quality/` | Testing strategy. |
| `docs/08-operations/` | IaC, deployment/rollback, SLOs/alerting, cost model, runbooks. |
| `docs/09-delivery/` | Roadmap, backlog, traceability, session log. |
| `infra/terraform/` | AWS single-node: EC2, Elastic IP, security group, instance role, S3 bucket/policy. |
| `deploy/` | Production Compose, Caddy, env template, backup/restore/smoke scripts. |
| `docker-compose.yml` | Local PostgreSQL 18, LocalStack S3, Mailpit, OTel Collector, Flyway. |
| `scripts/` | Bootstrap and validation helpers. |

Start a session by reading: this file → `docs/09-delivery/roadmap.md` §3–§6 → the doc for the
milestone you are implementing. Check `git log` for what changed since the last session.

## 3. Canonical facts (do not drift)

| Topic | Decision | Source |
|---|---|---|
| IDs | UUIDv7 (`uuidv7()`), exposed as UUID strings | ADR-0002 |
| DB changes | SQL migrations, forward-only; **no EF migrations, no EnsureCreated** | ADR-0002 |
| Transfers | **API-only S3**: chunked uploads to local staging then server-side S3; downloads streamed; no presigned URLs ever | ADR-0003 |
| S3 access | Only the EC2 instance role; bucket policy denies all other principals | D-30 |
| Hosting | Single EC2 Ubuntu 24.04, Docker Compose, Caddy TLS, PostgreSQL 18 container; no ALB/ECS/RDS | ADR-0010 |
| Counts | Increment only on completed stream; reservation for `max_downloads` | ADR-0005 |
| Staging | 16 MiB chunks, 10 GiB reserve, 3 concurrent sessions; complete scans/commits to S3 and can be retried | ADR-0003 |
| Malware | ClamAV scans the staged file **before** S3; infected files are never uploaded; scanner failure keeps `processing` | D-35 |
| Email | Microsoft 365 mailbox: POP inbound only; **SMTP AUTH 587** for sending | D-09 / ADR-0009 |
| Domain | `files.sacservices.cloud` (Network Solutions A record → Elastic IP); Caddy TLS | D-34 |
| Backups | Nightly `pg_dump` to `s3://<bucket>/backups/postgres/`, 35 days; RPO 24 h/RTO 4 h | D-31/D-33 |
| Host size | `t3.large` (8 GiB) for ClamAV + PostgreSQL + API/worker | D-35 |
| Link token | 32 bytes base64url (43 chars), SHA-256 at rest | ADR-0005 |
| Download key | Exactly 8 chars `[A-Za-z0-9]`, Argon2id verifier, versioned pepper, never logged | ADR-0005 |
| Machine auth | Entra client credentials only; **no API keys** are issued or stored | ADR-0004 |
| Retention | `finite` ⇔ `expires_at` not null; human default 30 d, absolute max 365 d, note required above default; indefinite only integrations | ADR-0006 |
| Integration files | Indefinite, `integration_only`, no public links, no portal visibility | Requirements §5.4 |
| Admins | Manage metadata/policy/legal hold; **no content access** | D-19 |
| Deletion | Logical first, physical async, tombstone retained; backups caveat | ADR-0008 |
| Jobs | Quartz in-memory + PG advisory locks; transactional outbox | ADR-0007 |
| Errors | RFC 9457, stable `code`, 404 for foreign link/file | ADR-0012 |
| Frontend | Angular standalone + Bootstrap 5.3 + MSAL; Spanish/English selector (Spanish default) | ADR-0013 |
| Links | Public 15 d / private 30 d (admin-defined, owner cannot change); private ACL by users/groups or all-tenant | ADR-0005 |

When a decision must change: add/update an ADR or `decisions.md`, update affected requirements,
stories, tests, schema, and contract **in the same change**, and note it in `AGENTS.md`.

## 4. Conventions

- **Language:** docs, code, identifiers, commits in English. User-facing UI strings in Spanish
  and English (Spanish default); emails use the recipient locale.
- **Commits:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`), small and focused.
- **Branches:** `feat/...`, `fix/...`; never commit directly to `main` once the team grows.
- **Tests:** every behavior change includes tests; authorization changes include denied cases.
  Integration tests use real PostgreSQL 18 + LocalStack S3, never an in-memory DB.
- **Secrets:** never commit them. `.env` is ignored; `.env.example` is placeholders only. No secret
  in logs, URLs, errors, fixtures, or screenshots.
- **Migrations:** immutable once applied; add a new `V<n>__...sql`. Test from an empty DB.
- **API:** contract-first. Update `docs/04-api/openapi/files-api.v1.yaml`, then the DTOs and tests.
- **Docs:** update the relevant doc and traceability matrix with the code. Stale docs are bugs.
- **Formatting:** `dotnet format`, Prettier/ESLint, `terraform fmt`, Redocly lint.
- **Warnings:** zero build warnings; analyzers enabled.

## 5. Commands

```bash
# Local dependencies (PostgreSQL 18, LocalStack S3, Mailpit, OTel Collector)
docker compose up -d

# Apply schema and seed
docker compose --profile tools run --rm flyway migrate

# Validate everything that exists today
make validate

# API/worker (after scaffolding)
dotnet run --project src/Files.Api
dotnet run --project src/Files.Worker

# Web (after scaffolding)
cd web && npm install && npm start

# OpenAPI
npx --yes @redocly/cli@latest lint docs/04-api/openapi/files-api.v1.yaml

# Terraform (single EC2 + S3)
cd infra/terraform && terraform init -backend=false && terraform validate

# Production deployment assets
deploy/docker-compose.prod.yml · deploy/Caddyfile · deploy/backup.sh
```

Local endpoints: API `http://localhost:8080`, Portal `http://localhost:4200`, LocalStack S3
`http://localhost:4566` (test/test), Mailpit UI `http://localhost:8025`, PostgreSQL
`localhost:5432` (`files`/`files`/`files`), OTel `localhost:4317/4318`.

## 6. Current state (update at the end of every session)

| Area | State |
|---|---|
| Product requirements, stories, RBAC | Done |
| Architecture + ADRs | Done |
| Data model + V1/V2 migrations | Done, validated on PostgreSQL 18.6 |
| OpenAPI contract | Done, Redocly-valid |
| Security designs + threat model | Done |
| Workflows (lifecycle/upload/download/retention) | Done |
| Testing strategy | Done |
| IaC skeleton | Done: single EC2 + S3, `terraform validate` (review on change) |
| Local stack, CI, scripts | Done |
| Deployment assets | Done: Compose, Caddy, backup/restore/smoke |
| Application code | **S3/M1 done** — EF Core mapping, domain entities, repositories, audit/outbox writers, Entra JWT auth (user + integration), JIT users, role policies, `/me`, integration admin CRUD + admin audit; S4+ pending |
| Terraform applied to AWS | Not started |

## 7. Next steps (in order)

1. ✅ **S1 / M0 (done):** .NET solution + Angular workspace, health endpoints, config validation,
   OTel wiring, local compose, and the API/worker Dockerfiles are in place.
2. ✅ **S2 / M1 (done):** EF Core mapping + repositories + audit/outbox writers; user/integration persistence.
3. ✅ **S3 / M1 (done):** Entra JWT validation, JIT provisioning, role policies, integration admin (Entra app binding; no API keys).
4. **S4–S5 / M2:** `IObjectStore`, `IChunkStore` staging, chunk endpoints, server-side S3 commit,
   completion verification, disk reserve, cleanup; integration tests with LocalStack S3.
5. **S6–S8:** portal, then links/public downloads.

Do not jump to UI before the upload/download vertical slice works end to end; it validates auth,
DB, S3, streaming, audit, and counts together.

## 8. Guardrails / common mistakes

- ❌ Any presigned S3 URL (upload or download) — clients must never touch S3 (D-30).
- ❌ Uploading a file to S3 before ClamAV returns clean when scanning is enabled.
- ❌ Assuming POP can send email; outbound uses SMTP AUTH 587.
- ❌ Proxying a 5 GiB upload without staging — the S3 commit must be retryable.
- ❌ Accepting chunks without checking staging free space/reserve.
- ❌ Publishing the API/PostgreSQL port or allowing SSH from 0.0.0.0/0.
- ❌ Incrementing counts when a grant is created — must be on completion.
- ❌ Treating `max_downloads` as a soft limit — reservations are atomic and enforce it.
- ❌ Storing an 8-char key in plaintext or logging it; ❌ putting it in the URL.
- ❌ Using `DateTime.UtcNow` instead of `TimeProvider`; ❌ comparing client-supplied time for expiry.
- ❌ Returning 403 for another user's file — use 404 to avoid existence oracles.
- ❌ Letting portal search see integration files.
- ❌ Giving admins content access "for support" without an ADR and audit design.
- ❌ Issuing API keys or storing integration client secrets; machine auth is Entra-only.
- ❌ Letting a legal-hold file expire or be deleted.
- ❌ Allowing a link owner to override the admin-defined public/private TTL.
- ❌ Far-future timestamps for indefinite retention.
- ❌ Auto-migrating the database on API startup or editing an applied migration.
- ❌ Committing `/opt/files/.env`, S3 keys, or Terraform state.
- ❌ Logging `Authorization`, `key`, `token`, `grant`, object keys, or filenames.
- ❌ Committing real secrets or `.env` files.

## 9. Definition of Done (per change)

- [ ] Requirement/story IDs referenced; acceptance criteria met.
- [ ] Unit + integration tests; denied-path tests for authorization; contract updated.
- [ ] No secrets; logs/metrics added for new failure modes.
- [ ] `make validate` passes; build has no warnings.
- [ ] Docs updated (requirements/stories/traceability/data model/OpenAPI/runbook as applicable).
- [ ] Migration is backward-compatible and tested from empty.
- [ ] Roadmap/backlog status updated.

## 10. Questions and escalation

- Product/security decisions: `docs/01-product/open-questions.md` (defaults documented). Do not
  silently invent a different behavior.
- Architecture changes: ADR required.
- Ambiguous requirement: ask the human; if blocked, proceed with the documented default and flag
  it in the PR/commit.
