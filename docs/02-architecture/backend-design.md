# Backend Design

## 1. Solution layout

```
src/
  Files.Domain/            # entities, value objects, enums, invariants, domain events
  Files.Application/       # use cases, DTOs, validators, ports, authorization policies
  Files.Infrastructure/    # EF Core, Dapper, S3, SMTP/Graph, ClamAV, crypto, telemetry, outbox
  Files.Api/               # minimal API endpoints, auth wiring, middleware, health
  Files.Worker/            # Quartz jobs, outbox dispatcher, advisory locks
tests/
  Files.UnitTests/
  Files.IntegrationTests/  # Testcontainers: PostgreSQL 18 + LocalStack S3 + Mailpit/ClamAV + mock OIDC
  Files.ContractTests/     # OpenAPI conformance
web/                       # Angular workspace
db/migrations/             # Flyway SQL (canonical DDL)
```

`Files.slnx` (or `Files.sln`) references all .NET projects. `Directory.Build.props` centralizes
`TargetFramework=net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, analyzers, and
deterministic builds. `global.json` pins SDK `10.0.x` with `rollForward=latestPatch`.

Dependency rule: `Domain ← Application ← Infrastructure`, `Api → Application + Infrastructure`.
Domain references no NuGet package except a primitives-free base. Application may reference
`Microsoft.Extensions.*` abstractions only; no EF/S3/SMTP/ClamAV types.

## 2. Project responsibilities

### Files.Domain

- Entities: `FileEntity`, `DownloadLink`, `DownloadLinkAcl`, `DownloadGrant`, `UploadSession`, `User`,
  `Integration`, `Tag`, `RetentionPolicy`, `NotificationPolicy`,
  `NotificationEvent`, `AuditEvent`.
- Value objects: `StorageKey`, `SanitizedFileName`, `DownloadKeyHash`, `TenantIdentity`,
  `Retention` (finite/indefinite).
- Enumerations: `FileStatus`, `OwnerType`, `AccessMode`, `RetentionMode`, `LinkStatus`,
  `ScanStatus`, `CredentialType`, `NotificationStatus`.
- Domain services: `FileAvailabilityPolicy`, `RetentionPolicyCalculator`,
  `DownloadKeyPolicy`, `FileNameSanitizer`.
- Domain events raised by aggregates and written to the outbox: `FileUploaded`,
  `FileAvailable`, `FileExpired`, `FileDeleted`, `LinkCreated`, `LinkRevoked`,
  `DownloadCompleted`, `DownloadDenied`.

### Files.Application

- One class per use case (`CreateUploadSessionHandler`, `CompleteUploadHandler`,
  `RedeemPublicLinkHandler`, `StreamFileHandler`, `SearchDirectoryHandler`, …), each returning
  `Result<T>`. `CreateLinkHandler` reads the admin TTLs and writes the ACL/all-tenant flag.
- Ports: `IObjectStore`, `IChunkStore`, `IEmailSender`, `IKeyGenerator`, `IVerifierHasher`,
  `IAuditWriter`, `IOutboxWriter`, `ICurrentActor`, `IUnitOfWork`, repositories, `TimeProvider`.
- Authorization: resource-based handlers (`FileOwnerRequirement`,
  `IntegrationFileAccessRequirement`, `PortalRoleRequirement`, `LinkGrantRequirement`).
- Validators (FluentValidation) for every request DTO; semantic business errors use `Result`
  with a stable error code (see error catalog) — exceptions are reserved for bugs.

### Files.Infrastructure

- EF Core 10 + Npgsql, snake_case naming, `FilesDbContext` mapping the Flyway schema. **No EF
  migrations**; schema changes are SQL migrations (Flyway) and the model is validated by
  integration tests.
- Dapper for atomic operations: link reservation, download counters, abuse buckets, outbox
  claim. SQL lives in `Infrastructure/Persistence/Sql/*.sql` as embedded resources.
- `S3ObjectStore` implementing `IObjectStore` with AWS SDK v4. Works against LocalStack S3 via
  `ServiceURL` + `ForcePathStyle`. The API/worker is the only caller; no presigned URLs are
  generated anywhere.
- `LocalChunkStore` implementing `IChunkStore`: creates a staging file with `O_EXCL`, performs
  `pwrite` at chunk offsets, fsyncs, verifies per-chunk hashes, enforces the free-space reserve,
  and deletes files on abort/completion. The staging path is a host bind mount on EBS.
- `S3CommitService`: verifies all chunks and total size/checksum, uploads the staging file with
  server-side multipart (64 MiB parts) or `PutObject`, verifies `HeadObject`, and is idempotent
  per session (a retry reuses staged bytes).
- Cryptography: Argon2id (`Konscious.Security.Cryptography.Argon2` or libsodium bindings) for
  link keys; SHA-256 for link/grant tokens; no API keys (Entra app tokens only).
- `SmtpEmailSender` (MailKit, STARTTLS 587) behind `IEmailSender`; `GraphEmailSender` is an
  optional adapter selected by `Notifications__Provider`.
- `DirectoryClient` (Graph) implementing `IDirectorySearch`: tenant-scoped user/group search and
  group membership checks with a 5-minute cache; only object ID, display name, and email returned.
- `ClamAvScanner` implementing `IClamAvScanner`: streams the staging file to `clamd` (INSTREAM),
  enforces timeouts, and returns clean/infected/failed; no file content is logged.
- OpenTelemetry instrumentation and redaction.

### Files.Api

- Minimal API with endpoint groups: `/v1/me`, `/v1/portal/*`, `/v1/integrations/*`,
  `/v1/public/*`, `/v1/admin/*`, `/health/*`, `/v1/config`.
- Authentication schemes: `BearerUser` (Entra user tokens) and `BearerIntegration` (client
  credentials tokens); JWT validation accepts both audiences, then the authorization layer
  enforces actor type per endpoint. There is no API-key scheme.
- Rate limiting: built-in limiter with named policies (`portal`, `integration`, `public-link`,
  `public-key-attempt`) partitioned by user/integration/IP.
- Middleware order: forwarded headers → request ID → exception/problem details → HSTS →
  authentication → authorization → rate limiter → endpoints.
- OpenAPI generated at build/startup, compared against `docs/04-api/openapi/files-api.v1.yaml`
  in contract tests.

### Files.Worker

- Generic host with Quartz scheduler (in-memory store) and jobs listed in
  [architecture.md](architecture.md#5-worker-components).
- `AdvisoryLockJobRunner`: wraps every job with `pg_try_advisory_lock(hash(jobName))`; skips
  when another replica holds it.
- Outbox dispatcher: claims rows with `FOR UPDATE SKIP LOCKED`, dispatches, marks processed;
  exponential backoff, dead-letter after 10 attempts.
- `ScanCommitJob`: claims `processing` files, scans the staging file, quarantines infected files,
  commits clean files to S3, deletes staging, and updates file/session state idempotently.
- Health endpoints on port 8080 (`/health/live`, `/health/ready`) for Docker/Caddy health checks.

## 3. Request pipeline and conventions

- **Validation:** DTO validation (shape, ranges, lengths) → 400 with `errors[]`; semantic
  errors (state, policy) → 422 with a stable code; auth → 401/403; missing/sensitive → 404.
- **Idempotency:** an endpoint filter for mutating integration calls reads `Idempotency-Key`,
  hashes the body, and uses `idempotency_records` inside the same transaction. A stored
  response (status + body + headers) is replayed. In-progress → 409 `idempotency_in_progress`.
- **Concurrency:** `If-Match: "<version>"` on metadata PATCH; mismatch → 412 `stale_version`.
- **Pagination:** cursor = base64url(JSON of sort key + id); `limit` 1..100 (default 25);
  response `{ items, nextCursor, hasMore }`.
- **Uploads:** chunk endpoints accept `application/octet-stream` with a 16 MiB limit (Kestrel and
  Caddy), write at the declared offset through `IChunkStore`, verify optional `X-Chunk-SHA256`,
  and return the updated session counters. `complete` performs chunk verification and the S3
  commit in a bounded background-free request (streaming 64 MiB parts).
- **Streaming:** `Results.Stream` with a custom `Stream` that triggers completion callbacks;
  disable response buffering; set `Content-Length` when known; support `Range` on the integration content endpoint
  (not for grant-based owner/link downloads).
- **Cancellation:** pass `HttpContext.RequestAborted`; abort S3 read and release reservations.

## 4. Configuration contract

Environment variables use `__` separators. Full template in `.env.example`.

| Key | Example | Notes |
|---|---|---|
| `ConnectionStrings__FilesDb` | `Host=localhost;Database=files;Username=files;Password=...` | Required. |
| `Entra__Instance` | `https://login.microsoftonline.com/` | |
| `Entra__TenantId` | GUID | Required; tokens from other tenants rejected. |
| `Entra__ApiClientId` | GUID | API app registration (audience). |
| `Entra__ApiAudience` | `api://<api-client-id>` | Accepted audience. |
| `Entra__PortalClientId` | GUID | SPA app registration (used for CORS/known clients). |
| `Entra__RequiredScope` | `Files.Access` | Delegated scope for user tokens. |
| `Auth__LinkKeyPepper` | 32+ random bytes base64 | Rotatable; see runbook. |
| `S3__Region` | `us-east-1` | |
| `S3__Bucket` | `files-prod-...` | Required. |
| `S3__ServiceUrl` | empty | Set for LocalStack S3. |
| `S3__ForcePathStyle` | `true` (local) / `false` (AWS) | |
| `S3__AccessKeyId` / `S3__SecretAccessKey` | empty | LocalStack only locally; in production the static key from `/opt/files/.env`. |
| `S3__UseSsl` | `true` | Never false in production. |
| `Storage__MaxFileSizeBytes` | `5368709120` | 5 GiB. |
| `Storage__ChunkSizeBytes` | `16777216` | 16 MiB client chunk. |
| `Storage__S3PartSizeBytes` | `67108864` | 64 MiB server-side S3 part. |
| `Storage__S3SinglePutThresholdBytes` | `67108864` | Below this, use `PutObject`. |
| `Storage__StagingPath` | `/var/lib/files/staging` | Host bind mount on EBS. |
| `Storage__StagingReserveBytes` | `10737418240` | Keep 10 GiB free at all times. |
| `Storage__MaxConcurrentSessions` | `3` | Per host (and per integration). |
| `Storage__UploadSessionTtlMinutes` | `60` | |
| `Storage__MaxChunkCount` | `10000` | |
| `Storage__AbortIncompleteAfterHours` | `24` | S3 multipart/staging cleanup. |
| `Abuse__MaxKeyAttemptsPerWindow` | `5` | |
| `Abuse__KeyAttemptWindowMinutes` | `15` | |
| `Abuse__KeyLockoutMinutes` | `15` | |
| `Abuse__MaxFailuresPerLinkPerHour` | `100` | |
| `Notifications__Provider` | `Smtp` | `Smtp` (M365 SMTP AUTH) or `Graph` fallback. |
| `Notifications__FromAddress` | `files@...` | |
| `Smtp__Host` | `smtp.office365.com` | STARTTLS on 587. |
| `Smtp__Port` | `587` | |
| `Smtp__UseStartTls` | `true` | Certificate validation required. |
| `Smtp__Username` | mailbox address | |
| `Smtp__Password` | secret | App password/password; never logged. |
| `Graph__TenantId` / `Graph__ClientId` / `Graph__ClientSecret` | GUID/secret | Directory search + optional Graph mail fallback. |
| `Graph__DirectoryEnabled` | `true` | Enables private-link directory search/membership. |
| `ClamAV__Host` / `ClamAV__Port` | `clamav` / `3310` | Internal Docker network. |
| `ClamAV__ScanTimeoutSeconds` | `900` | 5 GiB may take minutes. |
| `Features__MalwareScanning` | `true` | Scan before S3 commit. |
| `Features__PublicLinks` | `true` | Global switch. |
| `Features__MalwareScanning` | `false` | |
| `Cors__AllowedOrigins__0` | `https://files...` | Exact origins. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://otel-collector:4317` | |

Startup validation fails fast if required values are missing or malformed. Secrets are never
logged; `Program.cs` logs only key names and non-sensitive values.

## 5. Persistence rules

- One `DbContext` per request; `SaveChangesAsync` once at the end of the use case.
- Domain events are written to `outbox_messages` in the same transaction.
- Reads use `AsNoTracking()`; projections select only needed columns.
- Every owner-scoped query includes the owner predicate; repositories never expose
  `GetById` without an authorization-aware method (`GetOwnedFileAsync`).
- Search uses a single SQL statement with `ILIKE` + trigram index and cursor predicate.
- Optimistic concurrency via `version` column and EF concurrency token.
- Database time (`now()`) is authoritative for expiry comparisons; app `TimeProvider` is used
  for scheduling and tests. Never compare client-supplied times.

## 6. Error model

`ProblemDetails` with extensions: `code` (stable), `traceId`, `errors` (validation), and
`retryable` boolean. No stack traces, SQL, S3 keys, tokens, or key material. Full catalog:
[docs/04-api/error-catalog.md](../04-api/error-catalog.md).

## 7. Security implementation notes

- JWT validation: signature, `iss`, `aud`, `exp`, `nbf`, `tid` (must equal configured tenant),
  token type; clock skew ≤2 min. `oid`/`sub` mapped to `users.object_id`; `roles` mapped to
  policies. Reject tokens without a role that maps to the endpoint.
- Entra app auth: validate the JWT and map `azp`/`appid` to an active integration; reject disabled
  integrations; update `last_used_at` at most once per minute; never log the token or secret.
- Integration token auth: `azp`/`appid` must match `integrations.entra_client_id`; the role claim
  must include `Files.Integration`; the actual permission is read from Files; the service
  principal `oid` is stored for audit.
- Link key verification: load verifier by link token; Argon2id verify; on failure record attempt
  and apply throttling; on success issue grant. Verification happens even for invalid keys
  (unless locked out) to avoid timing oracles; generic 404 on any failure.
- Grants: store SHA-256(token); lookup by hash; check expiry, state, binding; atomic consume.
- File streaming: never trust client-provided storage keys; resolve file → storage key server
  side; sanitize `Content-Disposition` (RFC 5987 `filename*` + ASCII fallback).
- Audit: write in the same transaction for state changes; for streaming, write attempt before
  streaming and completion after.

## 8. Logging and telemetry

- Structured JSON logs with `TraceId`, `SpanId`, `RequestId`, `ActorType`, `ActorId`,
  `FileId` (when safe), `IntegrationId`, `Route`.
- Redaction: `Authorization`, `X-Api-Key`, `key`, `token`, `grant`, `cookie`, `Set-Cookie`,
  `S3__SecretAccessKey`; never log request bodies of upload/link endpoints.
- Traces: ASP.NET Core, HttpClient, EF Core, AWS SDK, Npgsql; custom spans for `chunk.write`,
  `s3.commit`, `s3.head`, `download.stream`, `key.verify`, `job.run`.
- Metrics: HTTP RED, uploads, downloads, link failures, reservation conflicts, job runs,
  outbox lag, S3 errors, email outcomes. Names in
  [observability-and-slos.md](../08-operations/observability-and-slos.md).

## 9. Scaffolding commands (future session, M0)

```bash
dotnet new sln -n Files
dotnet new classlib -n Files.Domain -o src/Files.Domain
dotnet new classlib -n Files.Application -o src/Files.Application
dotnet new classlib -n Files.Infrastructure -o src/Files.Infrastructure
dotnet new web -n Files.Api -o src/Files.Api
dotnet new worker -n Files.Worker -o src/Files.Worker
dotnet new xunit -n Files.UnitTests -o tests/Files.UnitTests
dotnet new xunit -n Files.IntegrationTests -o tests/Files.IntegrationTests
# add references, packages, Directory.Build.props, global.json, then:
dotnet build && dotnet test
```

See `scripts/bootstrap-solution.sh` for the maintained version.

## 10. Definition of done for backend changes

- Endpoint implemented with OpenAPI contract, validation, auth policy, audit, and tests.
- Unit tests for domain rules; integration tests with real Postgres and LocalStack S3 for persistence
  and storage; contract test for the response shape.
- No new warnings; analyzers clean; cancellation tokens honored.
- Metrics/logs added for new failure modes; no secrets in logs.
- Docs updated: API contract, error catalog, data model if schema changed, traceability.
