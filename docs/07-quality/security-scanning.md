# Security scanning and static guards

**Requirement:** NFR-009 · **Threat model:** E-04, I-03, S-04, T-04 · **CI:** `.github/workflows/ci.yml`

This session (S12/M7) turned the threat-model "Automated" verification list into CI-gated checks.
Everything below is reproducible locally without installing the tools (they run in Docker).

## 1. Static security guards (fast, runs in `make validate`)

`scripts/check-security-static.sh` fails the build on any of these regressions in the production
source (`src/`) or SPA (`web/src/`):

- `DateTime.UtcNow`/`DateTime.Now` (TimeProvider only).
- Presigned S3 URL issuance (`S3Presigner`, `GetPreSignedURL`, …).
- Microsoft Graph / POP usage (ADR-0014/0009: SMTP AUTH 587 only).
- `innerHTML` / `bypassSecurityTrust` / `document.write` (filenames are interpolated only, S-04).
- A structured log template emitting a filename, key, token, email, or object key (I-03).
- A mutating route mapped to an audit/usage/download-event resource (FR-AUD-002, T-04).

The log-template rule is also locked by `tests/Files.UnitTests/LogRedactionTests.cs` (reflects over
`LoggerMessage` templates in the loaded Files assemblies).

## 2. Secret scanning — gitleaks

- Config/allowlist: `.gitleaks.toml` (extends the default rule set; allowlists only the committed
  placeholder templates `.env.example` and `deploy/env.prod.example`, never a real secret).
- Run: `make scan-secrets` (or `./scripts/scan-secrets.sh`). Exit 0 = no findings.
- Regenerate the allowlist after a new finding: `./scripts/scan-secrets.sh --baseline`, review the
  fingerprints, and fold **only genuinely pre-existing, non-secret** entries into `.gitleaks.toml`.
- CI: `gitleaks/gitleaks-action@v2` in the `security-scanners` job.
- Report: `artifacts/gitleaks/report.json` (gitignored).

**No real secret is ever committed.** `.env` (and `/opt/files/.env`) stay gitignored; only
placeholder templates are checked in.

## 3. Dependency + image scanning — Trivy

- Dependencies/IaC: `make scan-deps` (or `./scripts/scan-deps.sh`) → `trivy fs --scanners vuln,secret`
  on the repo (npm lockfile, `.csproj`, Dockerfiles). Fails on HIGH/CRITICAL.
- Images: `make scan-images` (or `./scripts/scan-images.sh`) builds `files-api:local`/`files-worker:local`
  and runs `trivy image --scanners vuln,misconfig`. Report in `artifacts/trivy/`.
- CI: `security-scanners` job runs `scan-deps.sh` plus `trivy config` on the Dockerfiles.
- `dotnet list package --vulnerable --include-transitive` (already in the `backend` job) remains the
  .NET-specific dependency gate; the analyzer set (`Directory.Build.props`, `dotnet format`) is the
  SAST baseline.

## 4. DAST — OWASP ZAP

`make zap-baseline` (or `./scripts/zap-baseline.sh`) runs `zap-baseline.py` (image
`owasp/zap2docker-stable`) against `$ZAP_TARGET_URL` (default `http://localhost:8080`) and writes
`artifacts/zap/report.{json,html,md}`.

**Manual pre-launch procedure (S-04):**

1. Start a staging-like deployment (`docker compose up -d` locally, or the disposable host) and note
   its URL.
2. `ZAP_TARGET_URL=https://<host> ./scripts/zap-baseline.sh` (baseline pass records findings without
   failing on warnings).
3. Review `artifacts/zap/report.html`; remediate **every high-severity** alert, then re-run.
4. Run an authenticated active pass for the portal: set up ZAP's authentication context for the
   Entra test tenant, crawl `/files`, `/files/:id`, `/d/:token`, `/admin/*`, and re-run.

The SPA XSS posture is locked by the static guard (§1, no `innerHTML`) and by the CSP header added
to `deploy/Caddyfile` (`script-src 'self'`, `object-src 'none'`, `frame-ancestors 'none'`,
`connect-src`/`frame-src` limited to Entra).

## 5. Load / soak — k6

Scenarios live in `tests/perf/` (see its `README.md`). `make perf-smoke` validates every scenario and
runs a 1-VU/1-iteration pass with relaxed thresholds; the full 2×-baseline, ≥2 h soak (launch gate 4)
is documented in `tests/perf/README.md`. CI runs the smoke pass in the `performance-smoke` job.

## Baseline and artifacts

All reports are written under `artifacts/` (gitignored). The checked-in allowlist (`.gitleaks.toml`)
covers only the two placeholder templates; there are no other pre-existing findings.
