# Requirement Traceability Matrix

Every requirement maps to at least one story and one or more planned test suites. Update this
matrix in the same PR that changes a requirement, story, or test suite name. Test suite names are
targets for the implementation; when a suite is created, link its path.

## Functional requirements

| Requirement range | Stories | Test suites (planned) | Test layer |
|---|---|---|---|
| FR-ID-001..011 | US-001, US-002, US-021 | `AuthMatrixTests`, `EntraTokenValidationTests`, `JitProvisioningTests`, `TenantIsolationTests` | Integration, security |
| FR-M2M-001..014 | US-010..US-016 | `IntegrationAuthTests`, `ApiKeyLifecycleTests`, `IntegrationPermissionsTests`, `IdorTests`, `IpAllowlistTests` | Integration, security |
| FR-UP-001..018 | US-003, US-015, US-028 | `UploadSessionTests`, `ChunkUploadTests`, `StagingCapacityTests`, `ClamAvScanCommitTests`, `UploadVerificationTests`, `UploadCleanupJobTests`, `S3PolicyTests` | Integration, load |
| FR-FILE-001..005 | US-003, US-004, US-005, US-016 | `FileMetadataTests`, `FileUpdateTests`, `AuditWriteTests`, `SearchScopeTests` | Unit, integration |
| FR-SRCH-001..006 | US-007, US-008 | `TagTests`, `SearchTests`, `FilterSortTests`, `CursorPaginationTests` | Integration |
| FR-LINK-001..015 | US-020..US-024 | `LinkCreationTests`, `LinkRevocationTests`, `KeyVerificationTests`, `KeyThrottleTests`, `GrantConcurrencyTests`, `LinkStatsTests` | Unit, integration, security |
| FR-RET-001..008 | US-005, US-016, US-030 | `RetentionPolicyTests`, `RetentionOverrideTests`, `BulkMigrationTests`, `ExpirationPredicateTests` | Unit, integration |
| FR-NOT-001..010 | US-025 | `NotificationSchedulerTests`, `NotificationTemplateTests`, `EmailSenderTests`, `NotificationRetryTests`, `DeliveryStatusTests` | Integration |
| FR-DEL-001..009 | US-026, US-027, US-028 | `ExpirationJobTests`, `DeletionJobTests`, `DeletionRetryTests`, `ReconciliationTests`, `TombstoneTests` | Integration, chaos |
| FR-INT-001..011 | US-011, US-015, US-017, US-018, US-019 | `OpenApiContractTests`, `IdempotencyTests`, `IntegrationContentTests`, `EnterpriseFlowE2ETests` | Contract, integration, E2E |
| FR-ADM-001..008 | US-010, US-012, US-029, US-030 | `AdminAuthorizationTests`, `CredentialLifecycleTests`, `PolicyAdminTests`, `AdminAuditTests` | Integration, security |
| FR-AUD-001..003 | US-029 | `AuditAppendOnlyTests`, `DownloadEventTests`, `AuditRetentionTests` | Integration |
| FR-GEN-001..006 | US-021, US-031 | `HealthEndpointTests`, `ProblemDetailsTests`, `CorrelationIdTests`, `ErrorLeakageTests` | Unit, integration, contract |

## Non-functional requirements

| Requirement | Verification |
|---|---|
| NFR-001 availability | SLO dashboard, 30-day review, error-budget policy |
| NFR-002 latency | k6 metadata/chunk/download scenarios; p95 dashboards |
| NFR-003 throughput | k6 20 concurrent downloads / 5 chunked uploads on one EC2; soak test |
| NFR-004 capacity | load test at 5× baseline; DB/egress headroom review |
| NFR-005 isolation | `IdorTests` for every endpoint; auth matrix; code review |
| NFR-006 idempotency | `IdempotencyTests`, duplicate job runs, outbox replay tests |
| NFR-007 recovery | quarterly restore drill; RPO 24 h/RTO 4 h recorded in runbooks |
| NFR-008 durability | nightly `pg_dump` marker test; S3 durability; optional EBS snapshot verification |
| NFR-009 security | threat-model checklist; ZAP; tfsec; secret scanning; pen test |
| NFR-010 audit | audit within 60 s; `AuditWriteTests`; dashboard |
| NFR-011 observability | OTel traces/metrics; dashboards; alert tests |
| NFR-012 testability | CI thresholds; coverage gates |
| NFR-013 reproducibility | `docker compose up`; Terraform EC2/S3 apply; Compose deploy with Git SHA |
| NFR-014 configurability | config validation tests; admin settings tests |
| NFR-015 portability | LocalStack S3 integration suite; S3-compatible adapter |
| NFR-016 accessibility | axe-core E2E on core routes |
| NFR-017 browsers | Playwright full matrix nightly |
| NFR-018 cost visibility | cost tags, budgets, monthly review |
| NFR-019 data residency | Terraform region pin; no cross-region without approval |
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
| Infra | `infra/terraform/` | Deployments |
| Session state | `AGENTS.md`, `docs/09-delivery/roadmap.md` | Future sessions |

## Change control

1. A requirement change updates the FR/NFR row, the story, this matrix, and the tests in one PR.
2. A new error code updates the catalog, OpenAPI where applicable, and a negative test.
3. A schema change updates the migration, the data-model doc, and the EF mapping.
4. A contract change updates the OpenAPI file, DTOs, and contract tests; breaking changes need
   `/v2` or a deprecation plan.
5. An architectural decision gets an ADR; product-level decisions update `decisions.md`.
