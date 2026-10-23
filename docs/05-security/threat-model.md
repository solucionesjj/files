# Threat Model and Security Requirements

**Method:** STRIDE over the C4 container/trust-boundary model, plus abuse-case analysis for the
public link flow. **Status:** v1 baseline; revisit before launch and after any new data flow.
**Related:** [architecture.md](../02-architecture/architecture.md), [ADR-0004](../02-architecture/adr/ADR-0004-identity-model.md), [ADR-0005](../02-architecture/adr/ADR-0005-download-links-and-keys.md)

## 1. Scope

In scope: Portal, API, worker, PostgreSQL, S3, Entra ID integration, Graph email, public link
flow, admin surface, CI/CD, and the AWS runtime. Out of scope: the consuming enterprise product's
business authorization, end-user devices, and Microsoft's own infrastructure.

## 2. Assets

| Asset | Classification | Impact if compromised |
|---|---|---|
| File content | Confidential (customer documents) | Data breach, regulatory exposure. |
| File metadata (names, owners, sizes, tags) | Confidential | Inference of business relationships. |
| Human identities / tokens | Confidential | Account takeover, unauthorized access. |
| Integration credentials (API keys, Entra secrets) | Secret | Machine access to files and metadata. |
| Download keys and link tokens | Secret | Unauthorized downloads. |
| Audit and download events | Sensitive | Loss of non-repudiation. |
| Database and backups | Confidential | Full compromise. |
| S3 bucket and IAM role | Critical | Read/delete all objects. |
| Availability/SLO | Business | Reputational and operational cost. |

## 3. Actors and capabilities

| Actor | Capability |
|---|---|
| Anonymous internet user | Can call public endpoints, guess link tokens/keys, upload? (no upload without auth). |
| Authenticated tenant user | Own files, links; can attempt IDOR against other files. |
| Malicious/compromised integration | Valid credentials; attempts cross-integration access or quota abuse. |
| Link recipient | Has one valid link/key; may share or attempt brute force. |
| Rogue administrator | Can change policy, disable integrations, delete files; cannot read content by design. |
| Cloud/insider attacker | IAM/DB compromise, log access, backup access. |
| Automated scanners/bots | Enumeration, credential stuffing (limited by Entra), volumetric attacks. |

## 4. Trust boundaries

1. Internet ↔ ALB/API (TLS; untrusted input).
2. Browser ↔ Entra (OIDC; PKCE; tokens in the browser).
3. Consumer backend ↔ Entra/API (machine credentials).
4. API/Worker ↔ S3 (IAM role; private VPC/NAT; TLS).
5. API/Worker ↔ RDS (private subnets; TLS optional in-VPC but recommended).
6. Worker ↔ Graph (app permission restricted to one mailbox).
7. CI/CD ↔ AWS (OIDC federation preferred over long-lived keys).
8. Public visitor ↔ API (token + key + throttling).

## 5. STRIDE analysis

Likelihood/impact: L/M/H. Status is the v1 control; verification is how we prove it.

### Spoofing

| ID | Threat | L/I | Mitigation | Verification |
|---|---|---|---|---|
| S-01 | Forged/self-signed JWT or `alg=none` | L/H | Strict signature, `alg`, issuer, audience, lifetime validation; JWKS from Entra; tenant check. | Integration tests with tampered tokens; pen test. |
| S-02 | Stolen API key used by attacker | M/H | 256-bit keys, HMAC verifier, expiry, rotation, IP allowlist, rate limits, audit `last_used_at`, revocation. | Auth tests; key-leak tabletop. |
| S-03 | Forged integration identity via a different Entra app | L/H | `azp` must match an active integration; `Files.Integration` role required; DB permissions authoritative. | Token tests with unregistered app. |
| S-04 | Session/token theft via XSS | M/H | MSAL `sessionStorage`, no `innerHTML` for filenames, CSP, framework escaping, short token lifetime. | ZAP baseline; XSS tests; CSP review. |
| S-05 | Phishing with a lookalike public link | M/M | Fixed domain, TLS, clear link landing page, no credential prompt on `/d/*`, user training. | UX review; domain monitoring. |

### Tampering

| ID | Threat | L/I | Mitigation | Verification |
|---|---|---|---|---|
| T-01 | Client alters size/content type/checksum to bypass validation | M/M | Server-declared expectations, signed `Content-Type`, `HeadObject` verification after upload, optional checksum, allowlist. | Upload negative tests; S3 integration tests. |
| T-02 | Presigned URL reused for another key/part or after expiry | L/H | URL scoped to one key/upload ID/part, 15-min TTL, new key per file, no overwrite. | S3 policy tests; attempt cross-key upload. |
| T-03 | SQL injection | L/H | Parameterized EF/Dapper, no string concatenation, integration tests. | SAST/DAST; code review. |
| T-04 | Audit tampering | L/H | Append-only API, no UPDATE/DELETE grants for the app role on audit tables, transactional writes, DB audit logs. | DB permission tests; review. |
| T-05 | Filename/header injection (CRLF, path traversal) | M/M | Sanitizer, length limit, `Content-Disposition` RFC 5987 + ASCII fallback, `nosniff`, no filesystem paths. | Property-based tests; E2E download. |
| T-06 | Link counter manipulation | L/M | Atomic SQL reservations, state predicates, unique grant tokens. | Concurrency tests (parallel grants). |

### Repudiation

| ID | Threat | L/I | Mitigation | Verification |
|---|---|---|---|---|
| R-01 | User/integration denies an upload/download/deletion | M/M | Audit events with actor, IP hash, UA, trace ID; download events for denials; immutable via API; retention 24 months. | Audit E2E; log review. |
| R-02 | Admin denies a policy change/deletion | L/M | Admin audit with before/after, reason, trace; no update/delete endpoints. | Admin E2E. |

### Information disclosure

| ID | Threat | L/I | Mitigation | Verification |
|---|---|---|---|---|
| I-01 | IDOR: user/integration reads another owner's file | M/H | Ownership/grant predicate in every query; 404 for foreign IDs; integration scoping tests. | Automated IDOR suite for all endpoints. |
| I-02 | Storage keys/bucket exposed | L/M | Never serialized in DTOs; repository projections; error redaction. | Contract tests; DTO review. |
| I-03 | Credentials/tokens in logs, traces, errors | M/H | Redaction middleware, no body logging for auth/upload, structured logging allowlist, secret scanning in CI. | Log inspection test; CI secret scan. |
| I-04 | Error oracle reveals link/key validity | M/M | Uniform 404 body; constant-time verify; dummy hash for unknown tokens; comparable timing; rate limit. | Timing tests; code review. |
| I-05 | Enumeration via status codes | M/M | 404 for foreign resources and link/grant failures; 403 only in non-sensitive admin context. | API tests. |
| I-06 | S3 bucket public or misconfigured | L/H | Block Public Access, bucket policy denies `*` principal, Terraform checks, no ACLs. | `terraform plan` checks; AWS Config rule. |
| I-07 | Link metadata leaks filename to token holder | M/L | Token is a capability; landing page shows only name/size/expiry; no owner email; accepted risk. | Privacy review. |
| I-08 | Deleted data recoverable from backups | M/M | Documented 35-day backup window; no false erasure claims; encryption at rest; lifecycle. | DR doc review; legal sign-off. |
| I-09 | Public link key in browser history/referrer | L/M | Key POST body, never URL; `Referrer-Policy: no-referrer`; `no-store`; no third parties on `/d/*`. | E2E header checks. |
| I-11 | Link token or grant token captured in infrastructure access logs | M/M | Application logs emit only a fingerprint; ALB/CloudFront access logging is disabled for token-bearing paths (or restricted and retained ≤7 days if enabled); WAF logging, if added, must redact the URI. Grants are 120 s/single-use. | Log configuration review; staging log inspection. |
| I-10 | Email contents leak file data | L/L | Templates include only filename/expiry/portal URL; escaping; no attachments. | Template review; tests. |

### Denial of service

| ID | Threat | L/I | Mitigation | Verification |
|---|---|---|---|---|
| D-01 | Upload flood / denial of wallet | M/M | Per-integration quotas, max file size, rate limits, daily bytes, S3 lifecycle cleanup. | k6 load with quota breach; cost alarms. |
| D-02 | Download egress abuse on public links | M/H | Max downloads per link, rate limits, reservation limits, per-link monitoring, optional expiry. | Load tests; alert on spikes. |
| D-03 | Argon2 memory exhaustion via key attempts | M/M | Attempt throttling before hashing, lockout, bounded parallelism, small argon2 memory (19 MiB). | Stress test on link endpoints. |
| D-04 | Job starvation / duplicate work | L/M | Advisory locks, batch limits, isolated worker, outbox lag alerts. | Chaos test with two workers. |
| D-05 | Link token enumeration causes DB load | L/L | 256-bit tokens, rate limits, generic 404, indexed hash lookup. | Load test. |
| D-06 | Large file stalls API during download | L/M | Streaming, no buffering, timeouts, autoscaling; worker separate. | Load test with 5 GiB file. |

### Elevation of privilege

| ID | Threat | L/I | Mitigation | Verification |
|---|---|---|---|---|
| E-01 | User reaches admin endpoints | L/H | Entra app-role policy on every admin route, server-side checks, tests. | Authorization matrix tests. |
| E-02 | Integration accesses another integration's files | M/H | Explicit grants only, read-only; 404 otherwise; audit. | Cross-integration test suite. |
| E-03 | Container/IAM escape grants bucket-wide access | L/H | Separate task roles, prefix-scoped S3, no credentials in env for S3 (task role), read-only root FS, non-root user. | IAM policy review; `tfsec`. |
| E-04 | Supply-chain compromise of a dependency | M/H | Lockfiles, Dependabot, `dotnet list package --vulnerable`, Trivy image scan, pinned actions, SBOM. | CI pipeline. |

## 6. Abuse analysis: the 8-character key

Alphabet 62, length 8 → **62^8 = 218,340,105,584,896** keys ≈ **47.6 bits**.

| Scenario | Attempts/year | Probability of a hit (per link) |
|---|---|---|
| Throttled by link+IP only (5/15 min per IP, 1,000 IPs) | 1.75×10^8 | 8.0×10^-7 (0.00008%) |
| Global per-link lock (100 failures/hour) | 8.76×10^5 | 4.0×10^-9 (0.0000004%) |
| No controls (for comparison, 1,000 req/s) | 3.15×10^10 | 1.4×10^-4 (0.014%) |

Controls in v1: 5 failures per link+IP per 15 min, progressive delay, 15-min lockout, 100
failures/link/hour soft lock + alert, 30 grants/min/IP, monitoring. Even a successful guess only
yields one file, and the link can be revoked.

**Offline attack:** if both the database and the pepper leak, Argon2id (19 MiB, t=2) makes each
guess expensive; an attacker with 1,000 GPUs at ~1,000 guesses/s/GPU needs ~6.9 years on average
for one key. Without the pepper (DB-only leak), the same cost applies per key but offline; the
short key lifetime and revocation limit value. The pepper is stored in the runtime secret store,
not the database.

**Timing:** verification uses a constant-time Argon2id comparison; unknown tokens run a dummy
verify to reduce timing differences. Locked-out requests skip hashing (documented).

## 7. Security requirements checklist

| Requirement | Where enforced |
|---|---|
| TLS 1.2+ everywhere, HSTS | ALB/CloudFront, app middleware |
| Private S3, BPA, SSE, no ACLs | Terraform + bucket policy |
| Least-privilege IAM per task | Terraform task roles |
| No credentials in URLs/logs/errors | Redaction, API contract |
| Argon2id for link keys; HMAC for API keys | `Files.Infrastructure.Crypto` |
| 256-bit link/grant tokens | `IKeyGenerator` |
| Tenant + audience + signature validation | Auth handler |
| Server-side authorization on every query | Repositories/services + tests |
| 404 for foreign resources | Error mapper |
| Rate limits + key lockouts | ASP.NET limiter + Postgres abuse tables |
| Audit append-only | DB grants + no update routes |
| Malware gate (optional) | Scan status + download policy |
| Backup encryption + retention documented | ADR-0008, DR doc |
| Dependency/image scanning, SBOM | CI |
| No admin content access | RBAC matrix |
| Session storage (no tokens in localStorage) | SPA/ADR-0013 |

## 8. Residual risks (accepted in v1)

| Risk | Why accepted | Revisit |
|---|---|---|
| Public link metadata visible to token holder | Token is the capability; name/size needed for UX. | If customers require blind links. |
| 8-char key has limited entropy | Mandated by spec; mitigated by throttling/monitoring. | If spec changes to 12 chars. |
| Backups retain deleted content up to 35 days | Operationally required; disclosed. | Legal review. |
| Admins can delete but not read content | Least privilege; support may need break-glass later. | OQ-05. |
| Malware scanning optional | Cost/complexity; gate available. | Make mandatory at launch if required. |
| Single-region deployment | Cost/complexity; restore-based DR. | OQ-17. |
| No WAF in v1 | ALB + rate limits + validation; add AWS WAF as defense in depth. | Before public launch if budget allows. |
| Entra compromise | Out of scope; CA/MFA are the controls. | Conditional Access review. |

## 9. Verification plan

- **Automated:** IDOR suite per endpoint, auth matrix tests, concurrency tests for reservations,
  upload tampering tests, contract tests, secret scanning, SAST, dependency/image scanning,
  Terraform static analysis (`tfsec`/Checkov), ZAP baseline against staging.
- **Manual:** threat-model review per milestone; key-flow code review; AWS IAM review; pen test
  before public launch (scope: auth, links, IDOR, upload abuse, key brute force).
- **Operational:** daily review of denied-download spikes and key lockouts; quarterly access
  review of integrations and admin roles; annual DR/threat-model refresh.
