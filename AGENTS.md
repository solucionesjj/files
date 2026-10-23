# AGENTS.md — Working Agreement for Future Sessions

**Read this first.** This repository is the delivery package for **Files**, a secure file storage,
transfer, and integration platform. The design is complete; implementation starts at milestone M0.

**Status:** 2026-10-23 — documentation, data model, API contract, operations, and delivery plan
complete. No application code yet. Terraform skeleton under `infra/terraform/`.

---

## 1. What Files is

- **Portal** (Angular + Bootstrap) for tenant employees to upload, organize, retain, and share
  files.
- **API** (.NET 10) for machine integrations to upload/retrieve files without a user session.
- **Private S3** storage (multipart uploads, streamed downloads), PostgreSQL 18 metadata,
  Entra ID identity, Graph email, SigNoz telemetry.
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
| `infra/terraform/` | AWS skeleton: VPC, S3, RDS, ECS/ALB, alarms. |
| `docker-compose.yml` | Local PostgreSQL 18, LocalStack S3, Mailpit, OTel Collector, Flyway. |
| `scripts/` | Bootstrap and validation helpers. |

Start a session by reading: this file → `docs/09-delivery/roadmap.md` §3–§6 → the doc for the
milestone you are implementing. Check `git log` for what changed since the last session.

## 3. Canonical facts (do not drift)

| Topic | Decision | Source |
|---|---|---|
| IDs | UUIDv7 (`uuidv7()`), exposed as UUID strings | ADR-0002 |
| DB changes | SQL migrations, forward-only; **no EF migrations, no EnsureCreated** | ADR-0002 |
| Downloads | API streams; **no presigned GET**; grants are 120 s single-use | ADR-0003/0005 |
| Counts | Increment only on completed stream; reservation for `max_downloads` | ADR-0005 |
| Link token | 32 bytes base64url (43 chars), SHA-256 at rest | ADR-0005 |
| Download key | Exactly 8 chars `[A-Za-z0-9]`, Argon2id verifier, versioned pepper, never logged | ADR-0005 |
| API keys | `flk_<kid>_<secret>`, HMAC-SHA256 peppered, show once | ADR-0004 |
| Retention | `finite` ⇔ `expires_at` not null; `indefinite` ⇔ null; never a far-future date | ADR-0006 |
| Integration files | Indefinite, `integration_only`, no public links, no portal visibility | Requirements §5.4 |
| Admins | Manage metadata/policy; **no content access** by default | D-19 |
| Deletion | Logical first, physical async, tombstone retained; backups caveat | ADR-0008 |
| Jobs | Quartz in-memory + PG advisory locks; transactional outbox | ADR-0007 |
| Errors | RFC 9457, stable `code`, 404 for foreign link/file | ADR-0012 |
| Email | Microsoft Graph `sendMail` (provisional OQ-01) | ADR-0009 |
| Hosting | AWS ECS Fargate + RDS + S3 + ALB, Terraform | ADR-0010 |
| Frontend | Angular standalone + Bootstrap 5.3 + MSAL, Spanish UI | ADR-0013 |

When a decision must change: add/update an ADR or `decisions.md`, update affected requirements,
stories, tests, schema, and contract **in the same change**, and note it in `AGENTS.md`.

## 4. Conventions

- **Language:** docs, code, identifiers, commits in English. User-facing UI strings in Spanish
  (`es-CO`) unless OQ-14 changes it.
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

# Terraform
cd infra/terraform && terraform init -backend=false && terraform validate
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
| IaC skeleton | Done, `terraform validate` (review on change) |
| Local stack, CI, scripts | Done |
| Application code | **Not started** |
| Terraform applied to AWS | Not started |

## 7. Next steps (in order)

1. **S1 / M0:** run `scripts/bootstrap-solution.sh` (review it first), create the .NET solution and
   Angular workspace, health endpoints, config validation, OTel wiring; bring up compose; run CI.
2. **S2 / M1:** EF Core mapping + repositories + audit/outbox writers; user/integration persistence.
3. **S3 / M1:** Entra JWT validation, JIT provisioning, role policies, integration admin + API keys.
4. **S4–S5 / M2:** `IObjectStore`, upload sessions, presigned multipart, completion verification,
   cleanup; integration tests with LocalStack S3.
5. **S6–S8:** portal, then links/public downloads.

Do not jump to UI before the upload/download vertical slice works end to end; it validates auth,
DB, S3, streaming, audit, and counts together.

## 8. Guardrails / common mistakes

- ❌ Presigned S3 GET URLs for downloads — breaks counting/revocation (ADR-0003).
- ❌ Incrementing counts when a grant or URL is created — must be on completion.
- ❌ Treating `max_downloads` as a soft limit — reservations are atomic and enforce it.
- ❌ Storing an 8-char key in plaintext or logging it; ❌ putting it in the URL.
- ❌ Using `DateTime.UtcNow` instead of `TimeProvider`; ❌ comparing client-supplied time for expiry.
- ❌ Returning 403 for another user's file — use 404 to avoid existence oracles.
- ❌ Letting portal search see integration files.
- ❌ Giving admins content access "for support" without an ADR and audit design.
- ❌ Far-future timestamps for indefinite retention.
- ❌ Auto-migrating the database on API startup or editing an applied migration.
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
