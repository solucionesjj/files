# Requirement Traceability Matrix

Every requirement maps to at least one story and one or more planned test suites. Update this
matrix in the same PR that changes a requirement, story, or test suite name. Test suite names are
targets for the implementation; when a suite is created, link its path.

## Functional requirements

| Requirement range | Stories | Test suites (planned) | Test layer |
|---|---|---|---|
| FR-ID-001..011 | US-001, US-002, US-021 | `AuthApiTests` (integration, [tests/Files.IntegrationTests/Auth/AuthApiTests.cs](../../tests/Files.IntegrationTests/Auth/AuthApiTests.cs)); `RolePolicyTests`/`TokenClaimsTests` (unit); `AuthorizationMatrixTests` (integration, [tests/Files.IntegrationTests/Upload/AuthorizationMatrixTests.cs](../../tests/Files.IntegrationTests/Upload/AuthorizationMatrixTests.cs)) — every route × actor, 404-for-foreign vs 403-for-insufficient-role (I-01, E-01, NFR-005) | Integration, security |
| FR-M2M-001..014 | US-010..US-016 | `IntegrationRequirementHandlerTests`, `AuthApiTests` (integration, API-key issue/use/revoke); `AuthorizationMatrixTests` (integration, machine scope intersection + cross-integration 404) | Integration, security |
| FR-UP-001..018 | US-003, US-015, US-028 | `UploadGeometryTests`, `StagingCapacityTests`, `FileNamePolicyTests`, `ContentTypePolicyTests`, `ChecksumsTests`, `ClamAvProtocolTests`, `ClamAvScannerTests`, `ScanBackoffTests` (unit, [tests/Files.UnitTests/ClamAvProtocolTests.cs](../../tests/Files.UnitTests/ClamAvProtocolTests.cs)); `ContentDispositionTests` (unit, RFC 5987); `UploadApiTests`, `UploadCleanupTests`, `ScanCommitTests` (integration, [tests/Files.IntegrationTests/Upload/ScanCommitTests.cs](../../tests/Files.IntegrationTests/Upload/ScanCommitTests.cs)); `UploadTamperingTests` (integration, [tests/Files.IntegrationTests/Upload/UploadTamperingTests.cs](../../tests/Files.IntegrationTests/Upload/UploadTamperingTests.cs)) — traversal/CRLF/content-type injection, negative/zero/oversized, chunk oversize, whole-file checksum mismatch; EICAR→quarantine + scanner-down covered by `ScanCommitTests` | Unit, integration, load |
| FR-FILE-001..005 | US-003, US-004, US-005, US-016 | `PortalFilesApiTests` (integration, [tests/Files.IntegrationTests/Auth/PortalFilesApiTests.cs](../../tests/Files.IntegrationTests/Auth/PortalFilesApiTests.cs)); `PortalFileManagementTests` (integration, [tests/Files.IntegrationTests/Auth/PortalFileManagementTests.cs](../../tests/Files.IntegrationTests/Auth/PortalFileManagementTests.cs)); `FileEditDialog`/`FileDeleteDialog` specs (Angular unit); `AuditWriteTests`, `SearchScopeTests` (planned) | Unit, integration |
| FR-SRCH-001..006 | US-007, US-008 | `FileListQueryParsingTests` (unit, [tests/Files.UnitTests/FileListQueryParsingTests.cs](../../tests/Files.UnitTests/FileListQueryParsingTests.cs)); `PortalFilesApiTests` (integration); `TagTests`, `CursorPaginationTests` (planned) | Unit, integration |
| FR-LINK-001..017 | US-020..US-024 | `DownloadKeysTests`, `LinkTokensTests`, `KeyThrottleTests`, `LinkQueryParsingTests` (unit, [tests/Files.UnitTests](../../tests/Files.UnitTests)); `PublicLinkTests` (integration, [tests/Files.IntegrationTests/Upload/PublicLinkTests.cs](../../tests/Files.IntegrationTests/Upload/PublicLinkTests.cs)); `DownloadGrantTests` (integration, [tests/Files.IntegrationTests/Upload/DownloadGrantTests.cs](../../tests/Files.IntegrationTests/Upload/DownloadGrantTests.cs)); `LinkCreateDialog`/`PublicLink` specs (Angular unit); `links.spec.ts` (E2E, [web/e2e/links.spec.ts](../../web/e2e/links.spec.ts)) | Unit, integration, E2E, security |
| FR-RET-001..008 | US-005, US-016, US-030 | `RetentionRulesTests` (unit, [tests/Files.UnitTests/RetentionRulesTests.cs](../../tests/Files.UnitTests/RetentionRulesTests.cs)); `PortalFileManagementTests` (integration); `RetentionNotificationsTests` (integration, [tests/Files.IntegrationTests/Retention/RetentionNotificationsTests.cs](../../tests/Files.IntegrationTests/Retention/RetentionNotificationsTests.cs)); `ExpirationPredicateTests` (planned) | Unit, integration |
| FR-NOT-001..010 | US-025 | `NotificationTimingTests`, `NotificationRetryTests`, `EmailTemplateRendererTests`, `SmtpOptionsTests` (unit, [tests/Files.UnitTests](../../tests/Files.UnitTests)); `RetentionNotificationsTests` (integration, scheduling/dedupe/supersede + SMTP via Mailpit + retries/attempt states); `DeliveryStatusTests` (planned) | Unit, integration |
| FR-DEL-001..010 | US-026, US-027, US-028, US-030 | `PortalFileManagementTests` (integration, logical delete + legal hold); `RetentionNotificationsTests` (integration, legal hold blocks expiry + expiration job); `DeletionBackoffTests`, `StorageErrorsTests`, `ReconciliationScopeTests` (unit, [tests/Files.UnitTests](../../tests/Files.UnitTests)); `DeletionReconciliationTests` (integration, [tests/Files.IntegrationTests/Deletion/DeletionReconciliationTests.cs](../../tests/Files.IntegrationTests/Deletion/DeletionReconciliationTests.cs)) — physical deletion, idempotency, retry/backoff, permanent-failure alert, legal-hold skip, owner/admin/integration delete flows, tombstone purge, missing-object quarantine + orphan move | Unit, integration, chaos |
| FR-INT-001..011 | US-011, US-015, US-017, US-018, US-019 | `IdempotencyKeysTests`, `IntegrationQuotaTests`, `HttpRangeTests`, `IntegrationFileAccessTests` (unit, [tests/Files.UnitTests](../../tests/Files.UnitTests)); `IntegrationApiTests` (integration, [tests/Files.IntegrationTests/Upload/IntegrationApiTests.cs](../../tests/Files.IntegrationTests/Upload/IntegrationApiTests.cs)); `ContractSurfaceTests` (contract, [tests/Files.ContractTests/ContractSurfaceTests.cs](../../tests/Files.ContractTests/ContractSurfaceTests.cs)); `EnterpriseFlowE2ETests` (planned) | Contract, unit, integration |
| FR-ADM-001..008 | US-010, US-012, US-029, US-030 | `AuthApiTests` (integration, admin CRUD + API keys + role change); `RetentionNotificationsTests` (integration, policy versioning + migration + legal hold + notification policy + templates + admin audit); `AdminAuditReadTests` (integration, [tests/Files.IntegrationTests/Auth/AdminAuditReadTests.cs](../../tests/Files.IntegrationTests/Auth/AdminAuditReadTests.cs)) — admin+auditor read the audit/download/usage/settings surface, plain user 403 (FR-ADM-005/006/008); `AuthorizationMatrixTests` (integration, admin escalation denial + auditors read-only/no-download, E-01/FR-ADM-008). Admin file deletion + integration-grant management (the mutating part of FR-ADM-007) and settings PATCH are **deferred to v1.1** (S13/M7 owner decision; contract removed). | Integration, security |
| FR-AUD-001..003 | US-029 | `DownloadGrantTests` (integration, `download_events` completion row); `AdminAuditReadTests` (integration, audit + download-event read); `SecuritySurfaceTests` (contract, [tests/Files.ContractTests/SecuritySurfaceTests.cs](../../tests/Files.ContractTests/SecuritySurfaceTests.cs)) — audit/usage/download-event resources are append-only (FR-AUD-002); `LogRedactionTests` (unit, I-03) | Integration, contract |
| FR-GEN-001..007 | US-021, US-031, US-009 | `HealthEndpointTests`, `ProblemDetailsTests`, `CorrelationIdTests`, `LogRedactionTests` (unit, no secrets in log templates), `check-security-static.sh` | Unit, integration, contract |

## Non-functional requirements

| Requirement | Verification |
|---|---|
| NFR-001 availability | SLO dashboard, 30-day review, error-budget policy |
| NFR-002 latency | k6 metadata/chunk/download scenarios; p95 dashboards |
| NFR-003 throughput | k6 20 concurrent downloads / 5 chunked uploads on one EC2; soak test |
| NFR-004 capacity | load test at 5× baseline; DB/egress headroom review |
| NFR-005 isolation | `AuthorizationMatrixTests` for every route × actor (404 for foreign, 403 for insufficient role/scope); code review |
| NFR-006 idempotency | `IdempotencyTests`, duplicate job runs, outbox replay tests |
| NFR-007 recovery | quarterly restore drill; RPO 24 h/RTO 4 h recorded in runbooks |
| NFR-008 durability | nightly `pg_dump` marker test; S3 durability; optional EBS snapshot verification |
| NFR-009 security | `check-security-static.sh` (TimeProvider/presigned/Graph-POP/innerHTML/log redaction/audit append-only), gitleaks + Trivy + SAST in CI, ZAP baseline + CSP — see [security scanning](../07-quality/security-scanning.md); pen test before launch |
| NFR-010 audit | audit within 60 s; `AuditWriteTests`; 60-month audit/24-month download retention; dashboard |
| NFR-011 observability | OTel traces/metrics; dashboards; alert tests |
| NFR-012 testability | CI thresholds; coverage gates; `make validate` now includes `check-security-static` |
| NFR-013 reproducibility | `docker compose up`; host bootstrap script + Compose deploy with Git SHA |
| NFR-014 configurability | config validation tests; admin settings tests |
| NFR-015 portability | LocalStack S3 integration suite; S3-compatible adapter |
| NFR-016 accessibility | axe-core E2E on `/files` and `/files/:id` (zero violations) — [web/e2e/portal.spec.ts](../../web/e2e/portal.spec.ts) |
| NFR-017 browsers | Playwright E2E ([web/e2e/portal.spec.ts](../../web/e2e/portal.spec.ts), mocked Entra + mocked API); full browser matrix nightly |
| NFR-018 cost visibility | cost tags, budgets, monthly review |
| NFR-019 data residency | S3 bucket and host in the same approved AWS region (default `us-east-2`); no cross-region without approval |
| NFR-020 limits | quota and staging reserve tests; 429/507 codes |
| NFR-021 host capacity | disk/CPU/memory alerts; 80/90% thresholds |

## Documents and artifacts

| Artifact | Source of truth | Consumers |
|---|---|---|
| Requirements | `docs/01-product/requirements.md` | Stories, backlog, tests |
| Decisions | `docs/01-product/decisions.md`, ADRs | Architecture, implementation |
| Data model | `db/migrations/V1__initial_schema.sql` + `docs/03-data/data-model.md` | Backend, tests |
| API contract | `docs/04-api/openapi/files-api.v1.yaml` | Frontend, integrations, contract tests |
| Error codes | `docs/04-api/error-catalog.md` | API, frontend, tests |
| Lifecycle | `docs/06-workflows/file-lifecycle.md` | Backend, tests |
| SLOs/alerts | `docs/08-operations/observability-and-slos.md` | Ops, dashboards |
| Infra | `deploy/` (host bootstrap, Compose, Caddy, backups) | Deployments |
| Session state | `AGENTS.md`, `docs/09-delivery/roadmap.md` | Future sessions |

## Change control

1. A requirement change updates the FR/NFR row, the story, this matrix, and the tests in one PR.
2. A new error code updates the catalog, OpenAPI where applicable, and a negative test.
3. A schema change updates the migration, the data-model doc, and the EF mapping.
4. A contract change updates the OpenAPI file, DTOs, and contract tests; breaking changes need
   `/v2` or a deprecation plan.
5. An architectural decision gets an ADR; product-level decisions update `decisions.md`.
