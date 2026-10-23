# Automated Testing Strategy

**Requirement:** NFR-012 · **CI:** `.github/workflows/ci.yml` · **Definition of Done:** roadmap

## 1. Principles

1. Test behavior at the lowest level that can prove it, and always test authorization twice
   (allowed and denied).
2. Integration tests use **real PostgreSQL 18 and real S3-compatible storage** (LocalStack S3
   locally, real S3 in staging) — no in-memory database, no mocked storage for persistence tests.
3. Every requirement in `requirements.md` maps to at least one automated test (traceability).
4. Tests are deterministic: `TimeProvider` is injected, randomness is seeded/abstracted, and no
   test depends on the wall clock or network.
5. Security-critical paths get explicit abuse tests, not just happy paths.

## 2. Test pyramid

| Layer | Share | Tools | Scope |
|---|---|---|---|
| Unit | ~55% | xUnit, FluentAssertions | Domain invariants, policies, validators, crypto, sanitizers, state transitions. |
| Integration | ~30% | xUnit + Testcontainers + `WebApplicationFactory` | API endpoints, EF/Dapper, SQL, S3, outbox, jobs, auth handlers. |
| Contract | ~5% | Schemathesis / OpenAPI schema validation | Request/response/error shapes, status codes, headers. |
| E2E | ~5% | Playwright | Portal flows, link redemption, admin policy change. |
| Non-functional | ~5% | k6, ZAP, tfsec, Trivy | Performance, security, IaC, images. |

Coverage gates: ≥80% line on `Files.Domain` + `Files.Application`; ≥60% overall API;
no drop >2% in a PR without justification.

## 3. Unit tests

| Area | Cases |
|---|---|
| File lifecycle | Every allowed/forbidden transition, version bump, terminal states. |
| Download eligibility | Expired now/soon, indefinite, scan states, deleted, legal hold, quota. |
| Retention | finite/indefinite consistency, max/default, user override, integration defaults, bulk calculation. |
| Link keys | Alphabet/length, case sensitivity, CSPRNG usage (interface), Argon2 verify, constant-time wrapper. |
| Filename sanitizer | Control chars, CRLF, path separators, Unicode, RTL override, 512-char limit, 8.3 names, no extension. |
| API key format | Parse `flk_<kid>_<secret>`, reject malformed, prefix/length. |
| Grants | TTL, single-use, reservation release, source invariants. |
| Notifications | Due-time calculation, interval skipping, dedupe, supersede, template variable allowlist. |
| Pagination | Cursor encode/decode, sort key stability, invalid cursor. |
| Idempotency | Hash equality, replay, key reuse. |
| Authorization | Policy handlers, ownership predicates, tenant match. |

## 4. Integration tests

### Fixtures

- PostgreSQL 18 container, migrations applied with Flyway (same scripts as production).
- LocalStack S3 container with a private bucket and a lifecycle test config.
- A temporary staging directory per test (and a small reserve override to test `507`).
- Mock OIDC: a local JWKS endpoint + signed test tokens (RSA key generated once) to exercise
  validation without Entra; Mailpit (SMTP) for email; a ClamAV container for scanning tests.
- `WebApplicationFactory<Program>` with `TimeProvider` fake and deterministic key generation.
- Per-test schema reset via transaction rollback or `TRUNCATE ... CASCADE` in dependency order;
  migrations once per collection.

### Endpoint coverage matrix

For each endpoint, test: success, unauthenticated, wrong role/permission, foreign resource,
invalid input, rate limit (where applicable), and OpenAPI shape.

| Area | Key cases |
|---|---|
| `/me`, `/config` | Provisioning, role claims, tenant mismatch, no roles. |
| Portal upload | Session create, chunk upload/resume, duplicate/conflict chunks, complete, abort, size/type/checksum failures, disk reserve, scanning path. |
| Portal files | List filters/sort/pagination, detail, rename/tag/retention, If-Match conflict, delete. |
| Links | Create public/private, key shown once, list, revoke, stats, limits, policy disabled. |
| Public links | Metadata, wrong/right key, lockout, revoked/expired, concurrency with max=1, grant reuse. |
| Private links | Auth required, tenant mismatch, inactive user, valid flow. |
| Grants | Stream, expiry, single-use, file expires between grant and stream, disconnect. |
| Integration auth | Entra token mapping, disabled integration, missing permission, IP allowlist, API key rotation/expiry/revoke. |
| Integration files | Create (idempotent), complete, metadata, availability, content (range), delete, grants, cross-integration denial. |
| Admin | Integration CRUD/permissions/credentials, retention policy version, migration, notifications, templates, settings, audit filters, auditor read-only. |
| Jobs | Scheduler/sender/expiry/deletion/cleanup/reconcile, duplicate runs, advisory locks, retries. |
| Outbox | At-least-once, dead-letter, lag, replay. |

### Concurrency tests

- Two grants for `max_downloads=1` in parallel → one 201, one 404; `reserved` never exceeds max.
- Complete upload twice → same file, no duplicate effects.
- Expire while streaming → stream completes, next grant fails.
- Two workers execute a job → one skips (advisory lock).

### Migration/schema tests

- Apply all migrations to an empty DB; assert Flyway history and table/index counts.
- Apply to a copy of the previous release schema (once production exists) and run smoke queries.
- **EF drift test:** all mapped tables/columns/constraints exist in `information_schema`
  (curated list for critical tables), and no unmapped required columns.
- Constraint tests: invalid owner/retention/link shapes rejected (as validated in development).

## 5. Contract tests

- Validate the produced OpenAPI document against `docs/04-api/openapi/files-api.v1.yaml`
  (or generate the doc and diff schemas; the checked-in contract is authoritative).
- Validate representative responses against the schema (status, required fields, enums, formats).
- Schemathesis property-based tests on a staging instance for all `/v1` endpoints with auth
  fixtures: no 5xx on valid-shaped requests, documented 4xx only.
- Frontend DTOs generated with `openapi-typescript`; CI fails on contract changes without
  regeneration.

## 6. E2E (Playwright)

Scenarios (against API + LocalStack S3 + mocked Entra or test tenant):

1. Sign in, upload small and multi-chunk files (resume one), see them listed.
2. Search/filter/sort/paginate.
3. Rename, add/remove tags, extend retention, verify notification scheduling (UI/state).
4. Download own file; verify count increments only after completion.
5. Create public link, copy URL/key (shown once), redeem in a new context with a wrong then
   correct key, download.
6. Private link: unauthenticated redirect, then successful download; other tenant rejected.
7. Revoke link → old key fails.
8. Delete file → disappears, links fail.
9. Admin: create integration, issue key (shown once), change policy, view audit/usage.
10. Accessibility (axe) on `/files`, `/files/:id`, `/d/:token`, `/admin/*`.

Run on Chromium in PR CI; full browser matrix (Edge/Firefox/Safari) nightly.

## 7. Security tests

- **IDOR matrix:** every file/link/grant/session endpoint called by another user/integration →
  404 (or 403 only where documented), no data in body.
- **Auth matrix:** no token, expired, wrong audience/issuer/tenant, `alg=none`, app token on user
  route, user token on integration route.
- **Upload tampering:** declared vs actual size/type, cross-session chunk, duplicate/conflicting
  chunk, oversize chunk, out-of-order completion, staged-file tampering.
- **Public key:** lockout thresholds, timing distribution (t-test on success/failure paths),
  grant reuse, enumeration.
- **Headers:** no secrets in logs/traces (capture test), `Content-Disposition` encoding, no
  `X-Powered-By`, CORS allowlist, `nosniff`, CSP for the SPA.
- **Dependencies/IaC:** `dotnet list package --vulnerable`, `npm audit`, Trivy image scan, tfsec,
  secret scanning (gitleaks), SBOM generation.
- **DAST:** OWASP ZAP baseline + authenticated scan against staging before each release.

## 8. Performance and resilience

| Scenario | Target |
|---|---|
| Metadata list p95 | ≤500 ms at 50 RPS, 10k files |
| Download TTFB p95 | ≤800 ms, 50 concurrent streams |
| Upload throughput | ≥50 MB/s aggregate with 3 concurrent chunked sessions |
| 5 GiB download | API RSS <200 MiB, no buffering |
| 5 GiB chunked upload | staging disk <6 GiB per session, commit retry without re-upload |
| Disk reserve | uploads fail closed with 507 before the reserve |
| Link key attempts | 429/lockout adds <5 ms when locked |
| Job batch 500 | <60 s; no long locks |
| Outbox lag | <5 s at 100 events/s |

- k6 scripts in `tests/perf/` (metadata, upload orchestration, download, public link abuse).
- Fault injection: S3 5xx/timeout, SMTP 4xx/5xx, DB connection loss, ClamAV down, worker kill during deletion,
  clock skew — assertions on state, retries, and metrics.
- Soak test 2 h at 2× baseline before launch.

## 9. CI pipeline

| Stage | Runs on | Gate |
|---|---|---|
| Lint | Every PR | `dotnet format --verify-no-changes`, ESLint, Prettier |
| Build | Every PR | `dotnet build`, Angular build |
| Unit | Every PR | All pass; coverage gate |
| Integration | Every PR | PostgreSQL 18 + LocalStack S3 containers |
| Contract | Every PR | OpenAPI valid (Redocly), schemas match |
| Migration | Every PR | Fresh apply + drift test |
| Security static | Every PR | Vulnerable deps, secrets, tfsec |
| E2E (Chromium) | PR + main | Critical paths |
| Performance smoke | main / nightly | Budgets |
| Image scan + SBOM | main | No critical CVEs (exception process) |
| Deploy to EC2 | main/tag | Backup + migrations + compose + smoke tests |
| Deploy prod | Tag + approval | Same + rollback plan |

## 10. Test data and environments

- **No production data in tests.** Fixtures generate files (text/PDF placeholders) and users.
- Test users/integrations are seeded per test via the API or SQL helpers, never shared globally.
- Email is always mocked; no real messages from tests.
- Staging uses a dedicated Entra test tenant or mock OIDC; production uses the real tenant.
- Nightly tests run against an ephemeral environment created by Terraform where feasible.

## 11. Flaky tests

- Owner must fix or quarantine within 2 business days; quarantined tests are skipped with a
  tracking issue and do not count toward coverage.
- No arbitrary `Task.Delay`; use polling helpers with timeouts and explicit failure messages.
- Retries are allowed only for network-bound E2E tests (max 1) and must be visible in the report.

## 12. Manual verification (per release)

- Key-flow review: link key entry, admin credential issuance (secret shown once).
- Threat-model checklist (security doc §7).
- Restore drill (quarterly) and IAM/access review.
- Exploratory testing of uploads from slow networks and Safari.
