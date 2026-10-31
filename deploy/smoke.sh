#!/usr/bin/env bash
# Post-deploy smoke checks. Requires FILES_DOMAIN and (optionally) test credentials.
set -euo pipefail

DOMAIN="${FILES_DOMAIN:?set FILES_DOMAIN}"
BASE="https://${DOMAIN}"

fail() { echo "SMOKE FAIL: $*" >&2; exit 1; }

code="$(curl -fsS -o /dev/null -w '%{http_code}' "$BASE/health/live" || true)"
[ "$code" = "200" ] || fail "health/live returned $code"

code="$(curl -fsS -o /dev/null -w '%{http_code}' "$BASE/health/ready" || true)"
[ "$code" = "200" ] || fail "health/ready returned $code"

code="$(curl -fsS -o /dev/null -w '%{http_code}' "$BASE/v1/config" || true)"
[ "$code" = "200" ] || fail "config returned $code"

config_json="$(curl -fsS "$BASE/assets/config.json" || true)"
[ -n "$config_json" ] || fail "assets/config.json unreachable"
printf '%s' "$config_json" | grep -qE '"clientId"[[:space:]]*:[[:space:]]*"[^"]+"' \
  || fail "assets/config.json has an empty clientId"
printf '%s' "$config_json" | grep -qE '"tenantId"[[:space:]]*:[[:space:]]*"[^"]+"' \
  || fail "assets/config.json has an empty tenantId"

code="$(curl -sS -o /dev/null -w '%{http_code}' -I "http://${DOMAIN}/" || true)"
[ "$code" = "308" ] || [ "$code" = "301" ] || fail "HTTP->HTTPS redirect returned $code"

disk_free_kb="$(df -Pk /var/lib/files | awk 'NR==2 {print $4}')"
[ "$disk_free_kb" -gt 10485760 ] || fail "staging disk below 10 GiB reserve"

echo "SMOKE OK: health, config, SPA config, TLS redirect, disk reserve"
