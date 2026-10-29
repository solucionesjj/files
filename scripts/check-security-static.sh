#!/usr/bin/env bash
#
# Static security guards (CI-gated; also wired into `make validate`). These complement the runtime
# suites (AuthorizationMatrixTests, UploadTamperingTests, ScanCommitTests) and the external scanners
# (gitleaks, Trivy, OWASP ZAP). Any match is a violation of the canonical domain rules in AGENTS.md.
#
#   - no DateTime.UtcNow in production code (TimeProvider only)
#   - no presigned S3 URLs, no Microsoft Graph, no POP in production code
#   - no innerHTML / bypassSecurityTrust / document.write for filenames in the SPA
#   - no log template emitting a filename, key, token, email, or object key
#   - no mutating route mapped to audit/usage/download-event resources (append-only)
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FAILURES=0

fail() {
  printf '  \033[31m✗ %s\033[0m\n' "$1"
  FAILURES=$((FAILURES + 1))
}

pass() {
  printf '  \033[32m✓ %s\033[0m\n' "$1"
}

echo "== Static security checks =="

# 1. No wall-clock time in production code (TimeProvider only).
if grep -rnE 'DateTime\.(UtcNow|Now)\b' "$ROOT/src" --include='*.cs' | grep -v '/obj/' >/dev/null 2>&1; then
  fail "DateTime.UtcNow/Now found in src/ (use TimeProvider)"
  grep -rnE 'DateTime\.(UtcNow|Now)\b' "$ROOT/src" --include='*.cs' | grep -v '/obj/' || true
else
  pass "no DateTime.UtcNow/Now in src/"
fi

# 2. No presigned S3 URL issuance.
if grep -rniE 'GeneratePresignedUrl|GetPreSignedURL|PreSignedUrlRequest|S3Presigner|AWS4PreSignedUrlSigner' "$ROOT/src" --include='*.cs' | grep -v '/obj/' >/dev/null 2>&1; then
  fail "presigned S3 URL issuance found in src/"
  grep -rniE 'GeneratePresignedUrl|GetPreSignedURL|PreSignedUrlRequest|S3Presigner|AWS4PreSignedUrlSigner' "$ROOT/src" --include='*.cs' | grep -v '/obj/' || true
else
  pass "no presigned S3 URL issuance in src/"
fi

# 3. No Microsoft Graph or POP usage.
if grep -rniE 'Microsoft\.Graph|GraphServiceClient|IgraphServiceClient|Pop3Client|OpenPop|\.Pop\b' "$ROOT/src" --include='*.cs' | grep -v '/obj/' >/dev/null 2>&1; then
  fail "Graph/POP usage found in src/ (ADR-0014/0009: SMTP AUTH only)"
  grep -rniE 'Microsoft\.Graph|GraphServiceClient|IgraphServiceClient|Pop3Client|OpenPop|\.Pop\b' "$ROOT/src" --include='*.cs' | grep -v '/obj/' || true
else
  pass "no Graph/POP usage in src/"
fi

# 4. SPA renders filenames only by interpolation (S-04 / T-05).
if grep -rniE 'innerHTML|bypassSecurityTrust|document\.write|outerHTML|\[innerHTML\]' "$ROOT/web/src" 2>/dev/null | grep -v node_modules >/dev/null 2>&1; then
  fail "unsafe DOM/HTML rendering found in web/src/ (filenames must be interpolated)"
  grep -rniE 'innerHTML|bypassSecurityTrust|document\.write|outerHTML|\[innerHTML\]' "$ROOT/web/src" 2>/dev/null | grep -v node_modules || true
else
  pass "no innerHTML/bypassSecurityTrust/document.write in web/src/"
fi

# 5. Log templates never emit a sensitive field.
BANNED_PLACEHOLDER='Message = ".*\{((File|Storage|Object)[Kk]ey|FileName|Filename|Email|ApiKey|Token|Secret|Password|Authorization)\}'
if grep -rniE "$BANNED_PLACEHOLDER" "$ROOT/src" --include='*.cs' | grep -v '/obj/' >/dev/null 2>&1; then
  fail "log template emits a filename/key/token/email/object key"
  grep -rniE "$BANNED_PLACEHOLDER" "$ROOT/src" --include='*.cs' | grep -v '/obj/' || true
else
  pass "no log template emits a sensitive field"
fi

# 6. Audit/usage/download-event resources have no mutating route (FR-AUD-002, T-04).
if grep -rniE 'Map(Post|Put|Patch|Delete)\([^)]*(audit|usage|download-event)' "$ROOT/src/Files.Api" --include='*.cs' | grep -v '/obj/' >/dev/null 2>&1; then
  fail "mutating route mapped to an audit/usage/download-event resource"
  grep -rniE 'Map(Post|Put|Patch|Delete)\([^)]*(audit|usage|download-event)' "$ROOT/src/Files.Api" --include='*.cs' | grep -v '/obj/' || true
else
  pass "audit/usage/download-event routes are append-only"
fi

echo ""
if [ "$FAILURES" -ne 0 ]; then
  printf '\033[31m✗ %d static security check(s) failed.\033[0m\n' "$FAILURES"
  exit 1
fi

printf '\033[32m✓ all static security checks passed.\033[0m\n'
