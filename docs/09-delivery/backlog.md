# Backlog

Estimation: **S** ≤1 session, **M** 1–2 sessions, **L** 3+ sessions. Status: `Todo`, `Doing`,
`Done`, `Blocked`. Story IDs match [user-stories.md](../01-product/user-stories.md); requirement
IDs match [requirements.md](../01-product/requirements.md).

## Epics

| Epic | Outcome | Milestone |
|---|---|---|
| E1 Identity and access | Entra sign-in, roles, tenant isolation | M1 |
| E2 Human file management | Upload, list, search, tags, retention edits, download | M2/M3 |
| E3 Integrations | Machine identity, uploads, retrieval, deletion, quotas | M1/M5 |
| E4 Links and downloads | Public/private links, keys, grants, abuse controls, stats | M4 |
| E5 Lifecycle | Notifications, expiration, deletion, reconciliation | M6 |
| E6 Administration and audit | Policies, integrations, credentials, audit, usage | M1/M6 |
| E7 Operations | IaC, CI/CD, observability, DR, cost | M0/M7 |

## Stories

| ID | Story | Epic | Size | Milestone | Requirements | Depends on | Status |
|---|---|---|---|---|---|---|---|
| US-001 | Sign in with Entra ID | E1 | M | M1 | FR-ID-001..004, 008, 009 | S1 | Todo |
| US-002 | Role-based administration | E1 | S | M1 | FR-ID-010, 011 | US-001 | Todo |
| US-003 | Upload a file | E2 | L | M2/M3 | FR-UP-001..007, 009, 011, 015 | US-001, T-04 | Todo |
| US-004 | Browse and inspect files | E2 | M | M3 | FR-FILE-001/002, FR-RET-004 | US-003 | Todo |
| US-005 | Edit metadata and retention | E2 | M | M3 | FR-FILE-003/004, FR-RET-003/006 | US-004 | Todo |
| US-006 | Download my own file | E2 | M | M3 | FR-ID-006, FR-LINK-010/011 | US-004, US-021 | Todo |
| US-007 | Manage tags | E2 | S | M3 | FR-SRCH-001 | US-004 | Todo |
| US-008 | Search, filter, sort, paginate | E2 | M | M3 | FR-SRCH-002..006 | US-004 | Todo |
| US-009 | Accessible, responsive portal | E2 | M | M3 | FR-GEN-003/004, NFR-016 | US-004 | Todo |
| US-010 | Register and manage an integration | E3 | M | M1 | FR-M2M-001/008/009/010 | US-002 | Todo |
| US-011 | Entra client credentials + metadata | E3 | M | M5 | FR-M2M-002/003/007/008, FR-INT-003/005/006 | US-010, T-04 | Todo |
| US-012 | Issue, rotate, revoke API keys | E3 | M | M1/M5 | FR-M2M-004/005/006/013 | US-010 | Todo |
| US-013 | Rate limits and quotas | E3 | M | M5 | FR-M2M-014, FR-INT-010 | US-010 | Todo |
| US-014 | Delete an integration-owned file | E3 | S | M5 | FR-INT-002, FR-DEL-007/009 | US-011 | Todo |
| US-015 | Upload via integration API | E3 | L | M2/M5 | FR-UP-002..007, FR-INT-004/007 | US-003, US-011 | Todo |
| US-016 | Integration isolation and grants | E3 | M | M5 | FR-M2M-007/011/012, FR-FILE-005 | US-011 | Todo |
| US-017 | Idempotent retries | E3 | M | M5 | FR-INT-007, NFR-006 | US-015 | Todo |
| US-018 | Availability check and content | E3 | M | M5 | FR-INT-002/008/009 | US-015 | Todo |
| US-019 | Existing product end-to-end | E3 | M | M5 | FR-INT-011 | US-015..018 | Todo |
| US-020 | Create a public link with a key | E4 | M | M4 | FR-LINK-001..004, 015 | US-004 | Todo |
| US-021 | Redeem public/private links | E4 | L | M4 | FR-LINK-002/007..012, FR-ID-007 | US-020 | Todo |
| US-022 | Revoke or rotate a link | E4 | S | M4 | FR-LINK-006/013 | US-020 | Todo |
| US-023 | Abuse protection for keys | E4 | M | M4 | FR-LINK-009 | US-021, T-09 | Todo |
| US-024 | View download statistics | E4 | S | M4 | FR-LINK-014 | US-021 | Todo |
| US-025 | Expiration notifications | E5 | L | M6 | FR-NOT-001..010 | US-005, T-10 | Todo |
| US-026 | Automatic expiration/deletion | E5 | L | M6 | FR-DEL-001..006, FR-RET-008 | US-005, T-06 | Todo |
| US-027 | Manual deletion | E5 | S | M3/M6 | FR-DEL-007/009 | US-004 | Todo |
| US-028 | Cleanup and reconciliation | E5 | M | M6 | FR-UP-008/013, FR-DEL-006 | US-015, US-026 | Todo |
| US-029 | Audit trail | E6 | M | M1/M7 | FR-ADM-005/008, FR-AUD-001..003 | US-002, T-07 | Todo |
| US-030 | Configure global policies | E6 | L | M1/M6 | FR-RET-001..003/007, FR-NOT-001/003, FR-ADM-003/004/007 | US-002 | Todo |
| US-031 | Operate and observe the platform | E7 | L | M0/M7 | FR-GEN-001/006, NFR-001..020 | S1 | Todo |

## Enabling tasks (not user-facing)

| ID | Task | Size | Milestone | Notes |
|---|---|---|---|---|
| T-01 | Bootstrap .NET solution + Angular workspace | M | M0 | `scripts/bootstrap-solution.sh`, Directory.Build.props, global.json. |
| T-02 | Local stack (PostgreSQL 18, LocalStack S3, Mailpit, OTel) | S | M0 | `docker-compose.yml`, healthchecks, Flyway profile. |
| T-03 | CI pipeline | M | M0 | Build, tests, contract lint, migrations, terraform fmt/validate. |
| T-04 | EF Core persistence + repositories + outbox | L | M1 | DbContext, mapping, audit/outbox writers, DB tests. |
| T-05 | Authentication handlers and policies | L | M1 | Entra user/integration, API key, tenant check. |
| T-06 | Storage adapter (S3; LocalStack locally) and presigning | L | M2 | Multipart, HeadObject, ranges, delete, copy. |
| T-07 | Audit and download event writers | M | M1 | Transactional, append-only, no secrets. |
| T-08 | OpenAPI contract tests and DTO generation | S | M1 | Redocly lint, schema validation, TS types. |
| T-09 | Rate limiting + abuse counters | M | M4 | ASP.NET limiter + Postgres tables. |
| T-10 | Email sender (Graph) + template renderer | M | M6 | WireMock tests, retries, statuses. |
| T-11 | Worker scheduler + advisory locks + outbox dispatcher | M | M2 | Quartz in-memory, batch jobs, metrics. |
| T-12 | Observability (OTel, dashboards, alerts) | M | M0/M7 | Collector, SigNoz, SLOs, alert routes. |
| T-13 | Terraform modules and environments | L | M7 | Network, storage, DB, compute, alarms. |
| T-14 | Deployment pipeline + migrations job | M | M7 | ECR, ECS, circuit breaker, smoke tests. |
| T-15 | Threat model implementation checklist | M | M7 | IDOR/auth matrices, upload tampering, ZAP. |
| T-16 | Load/soak test scripts | M | M7 | k6: metadata, upload, download, abuse. |
| T-17 | DR drill and runbook rehearsal | M | M7 | Restore, RTO/RPO measurement. |
| T-18 | Docs upkeep + ADR log | S | ongoing | Update on every decision change. |
| T-19 | Existing-file backfill tooling + reconciliation | L | M7 | Mapping table, exporter, checksum verification, dual-run/cutover; see [migration plan](../06-workflows/existing-product-migration.md). |

## Suggested first vertical slice (S1–S5)

1. T-01/T-02/T-03 + health/config.
2. T-04/T-05 + US-001/US-002.
3. T-06 + US-003 (portal upload) → US-004 → US-006.
4. Integration tests for upload verification and auth denials.
5. Demo: sign in, upload 10 MiB, see it available, download via grant.

This slice proves auth, DB, S3, streaming, audit, and UI wiring before links/admin complexity.
