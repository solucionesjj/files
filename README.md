# Files

Secure file storage, transfer, and integration platform.

Files centralizes binary file storage in a **private Amazon S3 bucket** and exposes controlled
operations to two kinds of consumers:

- **People** — upload, organize, retain, and share files through the Files Portal (Microsoft Entra
  ID).
- **Enterprise systems** — create, retrieve, check, and delete files through a versioned API using
  machine identities, without storing file contents in their own applications.

Public download links require an **8-character download key** in addition to an unguessable link
token. Integration-owned files are isolated, indefinitely retained by default, and accessible only
to their owner or explicitly granted integrations. Retention, expiration notifications, physical
deletion, and audits are managed by the platform.

> **Status (2026-10-23):** design and delivery package complete; application code not started.
> See the [roadmap](docs/09-delivery/roadmap.md) and [AGENTS.md](AGENTS.md) for the next session.

## Documentation

| Area | Document |
|---|---|
| Requirements (FR/NFR, IDs, invariants) | [requirements.md](docs/01-product/requirements.md) |
| User stories and acceptance criteria | [user-stories.md](docs/01-product/user-stories.md) |
| Roles and authorization matrix | [rbac-and-personas.md](docs/01-product/rbac-and-personas.md) |
| Decisions | [decisions.md](docs/01-product/decisions.md) |
| Glossary | [glossary.md](docs/01-product/glossary.md) |
| Open questions and defaults | [open-questions.md](docs/01-product/open-questions.md) |
| Architecture (C4, flows, deployment) | [architecture.md](docs/02-architecture/architecture.md) |
| Backend / frontend design | [backend-design.md](docs/02-architecture/backend-design.md) · [frontend-design.md](docs/02-architecture/frontend-design.md) |
| ADRs | [adr/](docs/02-architecture/adr/) |
| Data model | [data-model.md](docs/03-data/data-model.md) · [migrations](db/migrations/) |
| API conventions and errors | [api-conventions.md](docs/04-api/api-conventions.md) · [error-catalog.md](docs/04-api/error-catalog.md) |
| OpenAPI 3.1 contract | [files-api.v1.yaml](docs/04-api/openapi/files-api.v1.yaml) |
| Entra ID / machine identity / threat model | [entra-id-authentication.md](docs/05-security/entra-id-authentication.md) · [machine-identity-and-authorization.md](docs/05-security/machine-identity-and-authorization.md) · [threat-model.md](docs/05-security/threat-model.md) |
| Lifecycle, upload/download, retention | [file-lifecycle.md](docs/06-workflows/file-lifecycle.md) · [upload-and-download.md](docs/06-workflows/upload-and-download.md) · [retention-notifications-deletion.md](docs/06-workflows/retention-notifications-deletion.md) |
| Testing strategy | [testing-strategy.md](docs/07-quality/testing-strategy.md) |
| Infrastructure, deployment, SLOs, cost, runbooks | [infrastructure-and-iac.md](docs/08-operations/infrastructure-and-iac.md) · [deployment-and-rollback.md](docs/08-operations/deployment-and-rollback.md) · [observability-and-slos.md](docs/08-operations/observability-and-slos.md) · [cost-model.md](docs/08-operations/cost-model.md) · [runbooks.md](docs/08-operations/runbooks.md) |
| Roadmap, backlog, traceability, session log | [roadmap.md](docs/09-delivery/roadmap.md) · [backlog.md](docs/09-delivery/backlog.md) · [traceability.md](docs/09-delivery/traceability.md) · [session-log.md](docs/09-delivery/session-log.md) |

## Repository layout

```
AGENTS.md                  # working agreement for future sessions
docs/                      # product, architecture, data, API, security, workflows, ops, delivery
db/migrations/             # canonical Flyway SQL (PostgreSQL 18)
infra/terraform/           # AWS skeleton: VPC, S3, RDS, ECS/ALB, alarms
infra/local/               # OTel collector and LocalStack S3 init config
scripts/                   # bootstrap and validation helpers
docker-compose.yml         # local dependencies
.github/workflows/ci.yml   # CI skeleton
```

## Local quickstart

Prerequisites: Docker, .NET SDK 10, Node.js 20.19+ (or 22 LTS), Terraform 1.9+ (IaC only).

```bash
cp .env.example .env          # review; local values are non-secret
docker compose up -d          # PostgreSQL 18, LocalStack S3, Mailpit, OTel Collector
docker compose --profile tools run --rm flyway migrate
```

| Service | URL / credentials |
|---|---|
| PostgreSQL | `localhost:5432` · db/user/password: `files` / `files` / `files` |
| LocalStack S3 API | `http://localhost:4566` · `test` / `test` · bucket `files-local` |
| LocalStack health | `http://localhost:4566/_localstack/health` |
| Mailpit (email UI) | `http://localhost:8025` (SMTP `localhost:1025`) |
| OTel Collector | `localhost:4317` (gRPC), `localhost:4318` (HTTP) |

The API and portal are not scaffolded yet. After S1 (see the roadmap):

```bash
dotnet run --project src/Files.Api     # http://localhost:8080
cd web && npm install && npm start     # http://localhost:4200
```

## Validation

```bash
make validate          # docs links/IDs, OpenAPI lint, compose config, Terraform fmt
make db-verify         # apply migrations to a throwaway PostgreSQL 18 container
```

## Core decisions at a glance

- **.NET 10 modular monolith + worker**, Angular + Bootstrap portal, PostgreSQL 18, EF Core +
  Dapper, Quartz in-memory scheduler with PG advisory locks.
- **Uploads** go directly to S3 via short-lived presigned multipart URLs; **downloads** stream
  through the API using 120-second single-use grants (no presigned GET, so counts and revocation
  are exact).
- **Public links** use a 256-bit token plus an 8-character key (Argon2id verifier), with
  throttling, lockouts, and monitoring.
- **Retention** is explicitly finite or indefinite (null expiry, never a sentinel date), versioned
  by policy; notifications and deletion are idempotent background jobs.
- **Integration files** are private, indefinitely retained, and cross-integration access requires
  an explicit read-only grant.
- **Entra ID** for humans (OIDC + PKCE, app roles) and client credentials for machines, with
  optional API keys as a compatibility path.

## License

Proprietary. All rights reserved.
