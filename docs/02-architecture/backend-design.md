# Backend Design

## 1. Solution layout

```
src/
  Files.Domain/            # entities, value objects, enums, invariants, domain events
  Files.Application/       # use cases, DTOs, validators, ports, authorization policies
  Files.Infrastructure/    # EF Core, Dapper, S3, Graph, crypto, telemetry, outbox
  Files.Api/               # minimal API endpoints, auth wiring, middleware, health
  Files.Worker/            # Quartz jobs, outbox dispatcher, advisory locks
tests/
  Files.UnitTests/
  Files.IntegrationTests/  # Testcontainers: PostgreSQL 18 + LocalStack S3 + mock OIDC/Graph
  Files.ContractTests/     # OpenAPI conformance
web/                       # Angular workspace
db/migrations/             # Flyway SQL (canonical DDL)
```

`Files.slnx` references all .NET projects. `Directory.Build.props` centralizes
`TargetFramework=net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, analyzers, and
deterministic builds. `global.json` pins SDK `10.0.x` with `rollForward=latestPatch`.

Dependency rule: `Domain ← Application ← Infrastructure`, `Api → Application + Infrastructure`.
Domain references no NuGet package except a primitives-free base. Application may reference
`Microsoft.Extensions.*` abstractions only; no EF/S3/Graph types.

## 2. Project responsibilities

### Files.Domain

- Entities: `FileEntity`, `DownloadLink`, `DownloadGrant`, `UploadSession`, `User`,
  `Integration`, `IntegrationCredential`, `Tag`, `RetentionPolicy`, `NotificationPolicy`,
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
  `RedeemPublicLinkHandler`, `StreamFileHandler`, …), each returning `Result<T>`.
- Ports: `IObjectStore`, `IEmailSender`, `IKeyGenerator`, `IVerifierHasher`,
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
  `ServiceURL` + `ForcePathStyle`.
- Cryptography: Argon2id (`Konscious.Security.Cryptography.Argon2` or libsodium bindings) for
  link keys; HMAC-SHA256 for API keys; SHA-256 for link/grant tokens.
- `GraphEmailSender` (Microsoft Graph SDK) behind `IEmailSender`.
- OpenTelemetry instrumentation and redaction.

### Files.Api

- Minimal API with endpoint groups: `/v1/me`, `/v1/portal/*`, `/v1/integrations/*`,
  `/v1/public/*`, `/v1/admin/*`, `/health/*`, `/v1/config`.
- Authentication schemes: `BearerUser` (Entra user tokens), `BearerIntegration` (client
  credentials tokens), `ApiKey`; a policy scheme routes `flk_` tokens to `ApiKey`, everything
  else to JWT validation with both audiences accepted, then the authorization layer enforces
  actor type per endpoint.
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
- Health endpoints on port 8080 (`/health/live`, `/health/ready`) for ECS.

## 3. Request pipeline and conventions

- **Validation:** DTO validation (shape, ranges, lengths) → 400 with `errors[]`; semantic
  errors (state, policy) → 422 with a stable code; auth → 401/403; missing/sensitive → 404.
- **Idempotency:** an endpoint filter for mutating integration calls reads `Idempotency-Key`,
  hashes the body, and uses `idempotency_records` inside the same transaction. A stored
  response (status + body + headers) is replayed. In-progress → 409 `idempotency_in_progress`.
- **Concurrency:** `If-Match: "<version>"` on metadata PATCH; mismatch → 412 `stale_version`.
- **Pagination:** cursor = base64url(JSON of sort key + id); `limit` 1..100 (default 25);
  response `{ items, nextCursor, hasMore }`.
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
| `Auth__ApiKeyPepper` | 32+ random bytes base64 | Rotatable; see runbook. |
| `Auth__LinkKeyPepper` | 32+ random bytes base64 | Rotatable; see runbook. |
| `S3__Region` | `us-east-1` | |
| `S3__Bucket` | `files-prod-...` | Required. |
| `S3__ServiceUrl` | empty | Set for LocalStack/LocalStack S3. |
| `S3__ForcePathStyle` | `true` (local) / `false` (AWS) | |
| `S3__UseSsl` | `true` | Never false in production. |
| `Storage__MaxFileSizeBytes` | `5368709120` | 5 GiB. |
| `Storage__MultipartThresholdBytes` | `67108864` | 64 MiB. |
| `Storage__PartSizeBytes` | `16777216` | 16 MiB. |
| `Storage__UploadSessionTtlMinutes` | `60` | |
| `Storage__PresignTtlMinutes` | `15` | |
| `Storage__MaxProxyUploadBytes` | `104857600` | 100 MiB staging path. |
| `Abuse__MaxKeyAttemptsPerWindow` | `5` | |
| `Abuse__KeyAttemptWindowMinutes` | `15` | |
| `Abuse__KeyLockoutMinutes` | `15` | |
| `Abuse__MaxFailuresPerLinkPerHour` | `100` | |
| `Notifications__Provider` | `Graph` | `Graph` or `Smtp` (future). |
| `Notifications__FromAddress` | `files-noreply@...` | |
| `Notifications__GraphTenantId` | GUID | |
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
- API key auth: parse `flk_<kid>_<secret>`; look up `kid`; reject disabled integrations before
  verification; verify HMAC with constant-time compare; update `last_used_at` at most once per
  minute; never log the secret.
- Integration token auth: `azp`/`appid` must match `integrations.entra_client_id`; role claim
  must include the required integration permission; `oid` is the service principal object ID
  and is stored for audit.
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
- Traces: ASP.NET Core, HttpClient, EF Core, AWS SDK, Npgsql; custom spans for `s3.presign`,
  `s3.complete`, `download.stream`, `key.verify`, `job.run`.
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
