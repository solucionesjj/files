# Files — Requirements Specification

**Status:** Baseline for implementation (v1 scope)
**Source:** `FILES_Product_Requirements_Technical_Specifications.md` v0.2
**Normative language:** MUST / MUST NOT / SHOULD / MAY (RFC 2119). Every requirement has a
stable ID used by stories, tests, and the backlog.

> This document resolves the source specification into implementable, testable requirements.
> Decisions that are not dictated by the source are recorded as `D-xx` in
> [decisions.md](decisions.md) and, where architectural, as ADRs in `docs/02-architecture/adr/`.
> Items needing stakeholder input are listed in [open-questions.md](open-questions.md); the
> requirements below assume the default answer marked there.

---

## 1. Scope

### 1.1 In scope (v1)

- Human file management through the Files Portal (Microsoft Entra ID).
- Public and private download links with an 8-character download key.
- Machine-to-machine upload, retrieval, and deletion through the Files API.
- Private Amazon S3 object storage with multipart uploads up to 5 GiB (configurable).
- Retention, expiration notifications, physical deletion, and reconciliation.
- Administration of integrations, global retention, notifications, quotas, and audit.
- Observability (OpenTelemetry → SigNoz), auditability, and operational runbooks.

### 1.2 Out of scope (v1)

- File content preview/editing (PDF, Office, images) — metadata and download only.
- Folder hierarchies — flat file list with tags (see `OQ-12`).
- Desktop sync clients, mobile apps.
- Multi-tenant SaaS operation — one Entra tenant per deployment.
- Cross-region active-active; disaster recovery is restore-based.
- End-user file sharing by email invitation (links only).
- Search inside file contents — filename and tags only.
- Billing/subscription management.

### 1.3 Actors

| Actor | Type | Description |
|---|---|---|
| Portal User | Human (Entra) | Tenant member with the `Files.User` app role (default for all tenant users). Owns files. |
| Portal Admin | Human (Entra) | `Files.Admin` app role. Global policy, integrations, overrides, deletion. |
| Auditor | Human (Entra) | `Files.Auditor` app role. Read-only access to audit and statistics. No file content. |
| Public Link Visitor | Anonymous | Possesses a public link token and its download key. |
| Integration | Machine (Entra or API key) | Registered enterprise application with explicit permissions and ownership boundary. |
| Scheduler | System | Background jobs for notifications, expiration, deletion, reconciliation. |

Detailed authorization rules: [rbac-and-personas.md](rbac-and-personas.md).

---

## 2. Functional requirements

Priority: **M** = must (v1 release blocker), **S** = should (v1 unless schedule slips),
**C** = could (post-v1). "Source" refers to sections of the input specification.

### 2.1 Identity and human access (FR-ID)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-ID-001 | The Portal MUST authenticate users exclusively through Microsoft Entra ID using OpenID Connect Authorization Code + PKCE. | M | §4.1 | US-001 |
| FR-ID-002 | The API MUST validate every human token's signature, issuer, audience, expiry, and configured tenant ID before serving data. | M | §4.1 | US-001 |
| FR-ID-003 | The system MUST identify a human by (`tenant_id`, `object_id`) and MUST NOT use email as a primary identity key. | M | §4.1 | US-001 |
| FR-ID-004 | Users MUST be provisioned automatically on first authenticated request (just-in-time) from token claims (`tid`, `oid`, `preferred_username`, `name`). | M | §4.1 | US-001 |
| FR-ID-005 | Authorization MUST be enforced server-side for every portal operation; UI hiding is not authorization. | M | §4.1 | US-001 |
| FR-ID-006 | A user MUST only list, read metadata of, modify, or download files they own, unless explicitly authorized through a valid link or an admin policy. | M | §4.1 | US-004, US-006 |
| FR-ID-007 | A private link MUST require a valid Entra token from the configured tenant and MUST additionally validate link status, file state, and any link restrictions. Tenant membership alone MUST NOT grant access to arbitrary files. | M | §4.1, §5.3 | US-021 |
| FR-ID-008 | Users MUST be able to sign out of the Portal, clearing local session state. | S | §4.1 | US-001 |
| FR-ID-009 | The API MUST expose the caller's effective identity, roles, and permissions at `GET /v1/me`. | M | §4.1 | US-001 |
| FR-ID-010 | Roles MUST be assigned through Entra app roles/groups and re-evaluated on every request; the system MUST NOT allow self-assigned roles. | M | §4.1 | US-002 |
| FR-ID-011 | The Portal MUST support an admin route guarded by `Files.Admin`; unauthorized users receive 403 and no data leakage. | M | §4.1, §3.3 | US-030 |

### 2.2 Machine identity and authorization (FR-M2M)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-M2M-001 | Each consuming application MUST be registered as an integration with a unique ID, slug, name, status, permissions, and credential metadata. | M | §4.2 | US-010 |
| FR-M2M-002 | Machine authentication MUST NOT depend on an interactive user session. | M | §3.2, §4.3 | US-011 |
| FR-M2M-003 | The preferred machine authentication MUST be Entra ID OAuth 2.0 client credentials, validated against the integration's registered Entra application identity. | M | §4.3 | US-011 |
| FR-M2M-004 | API keys MAY be issued for compatibility; keys MUST be random (≥256 bits), shown only once, stored only as a non-reversible verifier, and associated with exactly one integration. | M | §4.3 | US-012 |
| FR-M2M-005 | API keys MUST support expiration, revocation, rotation, and an overlap window in which two credentials are valid. | M | §4.3 | US-012 |
| FR-M2M-006 | Credentials MUST never appear in URLs, logs, error messages, traces, or analytics. | M | §4.3 | US-012 |
| FR-M2M-007 | An integration MUST only access files it owns or for which an explicit file-level grant exists. | M | §4.2 | US-011, US-016 |
| FR-M2M-008 | Integration permissions MUST be explicit (`file.write`, `file.read`, `file.delete`) and checked on every request. | M | §4.2 | US-011 |
| FR-M2M-009 | A disabled integration MUST be rejected immediately, including for otherwise valid credentials. | M | §4.2 | US-010 |
| FR-M2M-010 | Integrations MAY be restricted by IP allowlist (CIDR); requests outside it MUST be rejected. | S | §4.2 | US-010 |
| FR-M2M-011 | Integration-owned files MUST default to integration-only access, indefinite retention, no public link, and no human visibility. | M | §5.4 | US-016 |
| FR-M2M-012 | Every authorized retrieval and every rejected access attempt by an integration MUST be audited. | M | §5.4 | US-016 |
| FR-M2M-013 | API keys MUST be scoped to the integration's granted permissions; they MUST NOT act as a universal credential. | M | §4.3 | US-012 |
| FR-M2M-014 | General and credential-specific rate limits MUST apply to integration traffic. | M | §4.3 | US-013 |

### 2.3 File upload and storage (FR-UP)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-UP-001 | Binary content MUST be stored in a private S3 bucket; the database MUST store metadata and storage references only. | M | §7 | US-003 |
| FR-UP-002 | Object keys MUST be system-generated and MUST NOT contain the original filename. | M | §7 | US-003 |
| FR-UP-003 | The default maximum file size MUST be 5 GiB and MUST be configurable per deployment and per integration. | M | §7 | US-003, US-030 |
| FR-UP-004 | Uploads above the configured single-PUT threshold MUST use S3 multipart upload with presigned part URLs. | M | §7 | US-003 |
| FR-UP-005 | Upload authorization MUST be validated before presigned URLs are issued. | M | §7 | US-003 |
| FR-UP-006 | Presigned URLs MUST be short-lived (≤60 min), scoped to one object key and one operation, and MUST NOT permit overwrite of existing objects. | M | §7 | US-003 |
| FR-UP-007 | On completion, the system MUST verify the object exists, its size matches the declared size, its content type is permitted, and its checksum matches when supplied. | M | §7 | US-003 |
| FR-UP-008 | Incomplete multipart uploads MUST be aborted after a configurable period (default 24 h). | M | §7 | US-028 |
| FR-UP-009 | Objects MUST be encrypted at rest (SSE-S3 by default; SSE-KMS optional) and all transfers MUST use TLS. | M | §7 | US-031 |
| FR-UP-010 | S3 Block Public Access MUST remain enabled and bucket policies MUST deny public access. | M | §7 | US-031 |
| FR-UP-011 | The original filename MUST be stored as metadata and sanitized for display and `Content-Disposition`. | M | §7 | US-005 |
| FR-UP-012 | The system SHOULD reject files whose detected content type is not in the permitted allowlist. | S | §7 | US-003 |
| FR-UP-013 | S3 lifecycle rules MUST clean up incomplete uploads, staging objects, and orphaned objects. | M | §7 | US-028 |
| FR-UP-014 | Integrations MAY upload small files (≤100 MiB) through the API into a staging area before promotion to the final object. | S | §15 | US-015 |
| FR-UP-015 | A file MUST NOT become available until upload verification succeeds. | M | §7 | US-003 |

### 2.4 File metadata, tags, search (FR-FILE, FR-SRCH)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-FILE-001 | Each file record MUST contain: ID, original filename, storage key/bucket/provider, size, verified content type, owner type and owner ID, timestamps, retention mode and effective expiry, lifecycle status, tags, download count, last download timestamp, malware scan status, deletion/audit metadata. | M | §8 | US-003 |
| FR-FILE-002 | Sensitive storage references MUST never be exposed to unauthorized clients; API responses expose at most a logical file ID. | M | §8 | US-004 |
| FR-FILE-003 | Users MUST be able to rename a file, edit its tags, and change retention within policy. | M | §5.1, §13 | US-005 |
| FR-FILE-004 | Every metadata mutation MUST be recorded in the file audit trail. | M | §8, §17 | US-005 |
| FR-FILE-005 | Integration-owned files MUST NOT appear in portal search/listing unless an explicit policy authorizes human visibility. | M | §13 | US-016 |
| FR-SRCH-001 | Users MUST be able to assign multiple reusable tags per file, remove tags, and see existing tags for reuse. | M | §13 | US-007 |
| FR-SRCH-002 | Search MUST match filenames case-insensitively (partial match). | M | §13 | US-008 |
| FR-SRCH-003 | Listing MUST support filtering by one or more tags, lifecycle status, and expiration window. | M | §13 | US-008 |
| FR-SRCH-004 | Listing MUST support sorting by filename, creation date, and successful download count, ascending or descending. | M | §13 | US-008 |
| FR-SRCH-005 | Listing MUST be server-side paginated with a stable, documented ordering (cursor-based). | M | §13 | US-008 |
| FR-SRCH-006 | Search results MUST be authorization-filtered before serialization; no count or facet may leak foreign files. | M | §13 | US-008 |

### 2.5 Download links and public keys (FR-LINK)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-LINK-001 | A link MUST have: ID, 256-bit unguessable token, file reference, type (public/private), creation time, optional expiry, status (active/revoked/expired), optional max successful downloads, successful counter, creator, and authorization policy. | M | §12 | US-020 |
| FR-LINK-002 | Public links MUST require an 8-character download key in addition to the link token. | M | §6 | US-020 |
| FR-LINK-003 | The key MUST use `[A-Za-z0-9]`, exactly 8 characters, generated with a CSPRNG, case-sensitive, and verified server-side. | M | §6 | US-020 |
| FR-LINK-004 | The key MUST NOT be embedded in the URL, MUST NOT be logged, and MUST be stored as a secure verifier (Argon2id). | M | §6 | US-020 |
| FR-LINK-005 | The link token MUST NOT appear in logs in full; log only a hashed/truncated fingerprint. | M | §6 | US-020 |
| FR-LINK-006 | The key MUST be replaceable by revoking the link and creating a new one; the old key MUST stop working immediately. | M | §6 | US-022 |
| FR-LINK-007 | Link redemption MUST validate: link exists and is active, file is available, expiry, download limits, key, rate limits, and authorization — before any bytes are served. | M | §6 | US-021 |
| FR-LINK-008 | Error responses MUST NOT reveal whether the token, key, file, or limit was the failing factor. | M | §6 | US-021 |
| FR-LINK-009 | Failed key attempts MUST be throttled with progressive delays and temporary lockouts, and monitored. | M | §6 | US-023 |
| FR-LINK-010 | A link MUST never permit downloading an expired, deleted, quarantined, or unavailable file, regardless of link validity. | M | §12 | US-021 |
| FR-LINK-011 | Download counts MUST be incremented only on successful completion of a download, never on grant/URL issuance. A reservation mechanism MUST enforce max-download limits under concurrency. | M | §12 | US-021 |
| FR-LINK-012 | Concurrent behavior MUST be documented: a grant reserves one download slot for 120 seconds, converts to a success on completion, and releases on failure/expiry. | M | §12 | US-021 |
| FR-LINK-013 | Users MUST be able to view and revoke links for files they own; revoked links fail immediately. | M | §5.1 | US-022 |
| FR-LINK-014 | Users MUST be able to see per-link and per-file download statistics (count, last download, recent attempts). | S | §3.1 | US-024 |
| FR-LINK-015 | Admin-configurable policy MUST govern whether public links are allowed for user files. | S | §5.2 | US-030 |

### 2.6 Retention and expiration (FR-RET)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-RET-001 | Administrators MUST configure: human default retention, whether users may override, maximum retention, whether indefinite retention is allowed for humans, notification intervals, and admin override authority. | M | §9 | US-030 |
| FR-RET-002 | New human files MUST receive the current global default retention at creation and persist their own effective expiry. | M | §9 | US-003 |
| FR-RET-003 | Changing the global default MUST affect only newly created files unless an admin runs an explicit bulk migration. | M | §9 | US-030 |
| FR-RET-004 | Integration-owned files MUST default to indefinite retention, represented as `retention_mode = indefinite` with a null expiry — never a far-future timestamp. | M | §9 | US-016 |
| FR-RET-005 | Indefinite retention MUST NOT override legal requirements, admin restrictions, or explicitly authorized deletion. | M | §5.4, §9 | US-016, US-030 |
| FR-RET-006 | Retention changes MUST be validated against the active policy limits and audited. | M | §9 | US-005 |
| FR-RET-007 | Retention policy configuration MUST be versioned so existing files can be interpreted against the policy in force when their expiry was set. | S | §9 | US-030 |
| FR-RET-008 | Expired files MUST be non-downloadable immediately at logical expiry, before physical deletion. | M | §11 | US-026 |

### 2.7 Notifications (FR-NOT)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-NOT-001 | Administrators MUST configure multiple notification intervals before expiration (default 7, 3, 1 days). | M | §10 | US-030 |
| FR-NOT-002 | Notifications MUST be sent to the file owner's registered email address. | M | §10 | US-025 |
| FR-NOT-003 | Email templates MUST be configurable (subject/body) and MUST include file name, expiry date, and a safe portal reference; they MUST NOT include credentials or file contents. | M | §10 | US-025 |
| FR-NOT-004 | Notification scheduling and delivery attempts MUST be persisted with status. | M | §10 | US-025 |
| FR-NOT-005 | The system MUST prevent duplicate notifications for the same file/interval/expiry schedule. | M | §10 | US-025 |
| FR-NOT-006 | Retention changes MUST recalculate pending notifications (supersede no-longer-due events). | M | §10 | US-025 |
| FR-NOT-007 | Transient delivery failures MUST be retried with backoff; permanent failures MUST be recorded. | M | §10 | US-025 |
| FR-NOT-008 | Notifications MUST be suppressed for indefinite-retention and deleted files. | M | §10 | US-025 |
| FR-NOT-009 | Provider acceptance MUST be tracked distinctly from confirmed delivery; the UI/API MUST show this distinction. | M | §10 | US-025 |
| FR-NOT-010 | Email delivery failure MUST NOT block expiration or deletion. | M | §10, §11 | US-025 |

### 2.8 Lifecycle and deletion (FR-DEL)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-DEL-001 | A scheduled process MUST identify expiring files, send due notifications, and expire files past their effective expiry. | M | §11 | US-026 |
| FR-DEL-002 | Expired files MUST transition to `pending_deletion` and their S3 object MUST be deleted; downloads MUST already be blocked. | M | §11 | US-026 |
| FR-DEL-003 | Deletion MUST be idempotent and safe to run repeatedly; missing S3 objects count as success. | M | §11 | US-026 |
| FR-DEL-004 | Recoverable deletion failures MUST be retried with backoff and surfaced as metrics/alerts; the file MUST remain `pending_deletion`. | M | §11 | US-026 |
| FR-DEL-005 | The system MUST distinguish logical expiration, successful physical deletion, and deletion failure. | M | §11 | US-026, US-031 |
| FR-DEL-006 | A reconciliation job MUST detect missing objects, orphaned objects, and DB/S3 inconsistencies, and report/repair per policy. | M | §11 | US-028 |
| FR-DEL-007 | Owner-deleted (manual) files MUST follow the same physical deletion pipeline, with immediate download blocking. | M | §5.1 | US-027 |
| FR-DEL-008 | The product MUST document that backups may retain copies after logical deletion and MUST NOT claim immediate complete erasure. | M | §11 | US-031 |
| FR-DEL-009 | Audit records of deletion MUST include actor, reason, prior state, and result. | M | §17 | US-026 |

### 2.9 Integration API (FR-INT)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-INT-001 | The API MUST be versioned (`/v1`) with a documented compatibility policy. | M | §14 | US-011 |
| FR-INT-002 | The API MUST support: create upload session, complete upload, get metadata, download content, check availability, delete file (when authorized). | M | §14 | US-011, US-015 |
| FR-INT-003 | Every request MUST authenticate the integration and verify its permissions. | M | §14 | US-011 |
| FR-INT-004 | Integration file creation MUST assign ownership to the caller, set integration-only access, indefinite retention, no public link, and return a stable file ID plus upload instructions. | M | §14 | US-015 |
| FR-INT-005 | Retrieval MUST verify ownership or an explicit file-level grant. | M | §14 | US-016 |
| FR-INT-006 | The API MUST NOT expose S3 credentials, bucket listing, unrestricted URLs, or permanent public URLs. | M | §14 | US-011 |
| FR-INT-007 | Mutating operations that clients may retry MUST support idempotency via `Idempotency-Key`, scoped per integration, with stored results. | M | §14 | US-017 |
| FR-INT-008 | The API MUST expose a lightweight availability endpoint that does not transfer content. | M | §14 | US-018 |
| FR-INT-009 | Integration download of expired/quarantined/deleted files MUST return a stable, non-revealing error. | M | §14, §11 | US-018 |
| FR-INT-010 | The API MUST enforce per-integration quotas (max file size, concurrent uploads, daily bytes, request rate). | S | §18 | US-013 |
| FR-INT-011 | The integration contract MUST document the recommended flow for the existing enterprise product (§15) end to end. | M | §15 | US-019 |

### 2.10 Administration and audit (FR-ADM, FR-AUD)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-ADM-001 | Admins MUST manage integrations: create, update, enable/disable, set permissions, configure IP allowlists. | M | §3.3 | US-010 |
| FR-ADM-002 | Admins MUST issue, list, rotate, and revoke integration credentials; secrets are shown once. | M | §4.3 | US-012 |
| FR-ADM-003 | Admins MUST manage retention policy versions and apply explicit bulk retention migrations. | M | §9 | US-030 |
| FR-ADM-004 | Admins MUST manage notification policies and email templates. | M | §10 | US-030 |
| FR-ADM-005 | Admins MUST view and filter audit events (file, download, admin, credential). | M | §3.3 | US-029 |
| FR-ADM-006 | Admins MUST view storage/download usage and limits. | S | §3.3 | US-031 |
| FR-ADM-007 | Admins MUST be able to override retention or delete a file with a recorded reason. | S | §9, §11 | US-030 |
| FR-ADM-008 | Auditors MUST have read-only audit access without permission to download content. | S | §3.3 | US-029 |
| FR-AUD-001 | Every upload, metadata/retention/link change, download attempt/result, deletion, credential event, and admin action MUST be audited with actor, timestamp, result, and correlation ID. | M | §17, §18 | US-029 |
| FR-AUD-002 | Audit records MUST be append-only through the application; no update/delete API is exposed. | M | §17 | US-029 |
| FR-AUD-003 | Audit retention MUST be configurable (default 24 months) and documented. | S | §18 | US-029 |

### 2.11 Health, errors, and UX (FR-GEN)

| ID | Requirement | Pri | Source | Stories |
|---|---|---|---|---|
| FR-GEN-001 | The API MUST expose liveness and readiness endpoints separate from business endpoints. | M | §18 | US-031 |
| FR-GEN-002 | Errors MUST use RFC 9457 `application/problem+json` with a stable `code`, `traceId`, and no sensitive detail. | M | §6, §18 | US-021 |
| FR-GEN-003 | The Portal MUST show upload progress, validation errors, empty states, and actionable recoverable errors. | S | §3.1 | US-003 |
| FR-GEN-004 | The Portal MUST be responsive (Bootstrap 5.3) and meet WCAG 2.1 AA for core flows. | S | §16 | US-009 |
| FR-GEN-005 | All timestamps in APIs MUST be ISO 8601 UTC; UI localizes to the user's timezone. | M | §8 | US-004 |
| FR-GEN-006 | The API MUST return a correlation/trace ID on every response and honor `X-Request-Id` when valid. | S | §18 | US-031 |

---

## 3. Non-functional requirements

Targets are measurable and become SLOs in [observability-and-slos.md](../08-operations/observability-and-slos.md).

| ID | Category | Requirement / target | Pri |
|---|---|---|---|
| NFR-001 | Availability | API + Portal monthly availability ≥ 99.9% (≤43.8 min downtime). | M |
| NFR-002 | Latency | Metadata reads p95 ≤ 300 ms; list/search p95 ≤ 500 ms; upload-session creation p95 ≤ 500 ms; download TTFB p95 ≤ 800 ms (S3 warm). | M |
| NFR-003 | Throughput | Sustain 50 concurrent downloads and 20 concurrent multipart uploads per environment; aggregate 100 MB/s ingress. | M |
| NFR-004 | Capacity | Baseline: 10,000 files, 500 GiB stored, 50 GiB/month ingress, 200 GiB/month egress. Design headroom ×5. | S |
| NFR-005 | Isolation | Strict owner/integration isolation; every query filtered by owner or explicit grant; IDOR tests mandatory. | M |
| NFR-006 | Idempotency | Background jobs and retried API mutations are idempotent; duplicate execution causes no duplicate files/emails/audit actions. | M |
| NFR-007 | Recovery | RPO ≤ 15 min (PITR), RTO ≤ 4 h; quarterly restore drill. | M |
| NFR-008 | Durability | S3 durability (11 nines) plus versioning/replication decision documented; DB daily snapshots + PITR 35 days. | M |
| NFR-009 | Security | Least-privilege IAM, private buckets, TLS 1.2+, Argon2id/HMAC verifiers, no secrets in code/logs/URLs. | M |
| NFR-010 | Audit | All security-relevant events auditable within 60 s of occurrence; retention configurable (default 24 months). | M |
| NFR-011 | Observability | 100% of requests traced; RED metrics per endpoint; job success/lag metrics; alerting to on-call. | M |
| NFR-012 | Testability | Automated unit/integration/E2E/contract tests in CI; coverage ≥80% line on Domain/Application. | M |
| NFR-013 | Reproducibility | One-command local stack; immutable container images; IaC-managed infrastructure; no manual prod steps. | M |
| NFR-014 | Configurability | Max file size, retention, quotas, rate limits, timeouts, and notification intervals configurable without code changes. | M |
| NFR-015 | Portability | Storage abstraction allows S3-compatible backends (LocalStack S3, LocalStack S3) for local/test. | S |
| NFR-016 | Accessibility | WCAG 2.1 AA on upload, list, detail, link management, and admin core flows. | S |
| NFR-017 | Browsers | Latest 2 versions of Edge, Chrome, Firefox, Safari; no IE. | S |
| NFR-018 | Cost visibility | AWS cost allocation tags per environment/service; monthly cost report with storage/egress/request breakdown. | S |
| NFR-019 | Data residency | All data at rest in the approved AWS region (default `us-east-1`); no cross-region copies without approval. | S |
| NFR-020 | Limits | Per-integration and global quotas enforced; 429 responses include `Retry-After`. | M |

---

## 4. Domain rules (invariants)

These are enforced in the database where possible and in domain services otherwise.

1. A file has exactly one owner: `owner_type = human` with `owner_user_id`, or
   `owner_type = integration` with `owner_integration_id`.
2. `retention_mode = indefinite` **iff** `expires_at IS NULL`; `finite` requires `expires_at`.
3. Integration-owned files always have `retention_mode = indefinite` at creation and
   `access_mode = integration_only`; only an admin override may change retention.
4. A file is downloadable **iff** `status = available` AND malware status is `clean` or
   `not_scanned` (when scanning disabled) AND (`expires_at IS NULL` OR `expires_at > now()`)
   AND its owner/integration/link authorization passes.
5. A public link requires a non-null `download_key_hash`; a private link must not have one.
6. `download_links.status` transitions `active → revoked` or `active → expired`; terminal
   states never return to `active`. Expiry is evaluated lazily even if the status column lags.
7. `successful_downloads ≤ max_downloads` when a maximum is set; reserved grants count toward
   the maximum until released.
8. File lifecycle transitions follow [file-lifecycle.md](../06-workflows/file-lifecycle.md);
   `deleted` is terminal (metadata tombstone retained per audit policy).
9. `storage_key` is unique and immutable for the lifetime of the object.
10. An integration cannot be granted access to a file it does not own when either party is
    disabled, and grants are always read-only (`file.read`).
11. A user's tenant (`tid`) must match the configured tenant for portal and private-link access.
12. Deletion is logical first, physical second; a deleted file never re-enters `available`.

---

## 5. Traceability

The requirement ID → story → test mapping is maintained in
[traceability.md](../09-delivery/traceability.md). Story IDs referenced above are defined in
[user-stories.md](user-stories.md). Any requirement change must update that matrix in the same
commit.

---

## 6. Open items

- Unresolved decisions and defaults: [open-questions.md](open-questions.md).
- Decision log: [decisions.md](decisions.md).
- Architecture decisions: `docs/02-architecture/adr/`.
