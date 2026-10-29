#!/usr/bin/env bash
#
# Secret scanning (I-03 / NFR-009) via gitleaks, run inside Docker so no local install is needed.
#
#   ./scripts/scan-secrets.sh            # fail on any finding (CI gate)
#   ./scripts/scan-secrets.sh --baseline # regenerate the allowlisted baseline (review before commit)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${GITLEAKS_IMAGE:-zricethezav/gitleaks:latest}"
OUT="$ROOT/artifacts/gitleaks"
mkdir -p "$OUT"

GITLEAKS_ARGS=(detect --source /src --config /src/.gitleaks.toml --redact --verbose)

if [ "${1:-}" = "--baseline" ]; then
  echo "Regenerating gitleaks baseline to .gitleaks.toml (review before committing)."
  docker run --rm -v "$ROOT":/src -w /src "$IMAGE" detect \
    --source /src --config /src/.gitleaks.toml --redact \
    --baseline-path /src/.gitleaks-report.json --report-format json --report-path /src/.gitleaks-report.json
  echo "Baseline written to .gitleaks-report.json; add its fingerprints to .gitleaks.toml [allowlist]."
  exit 0
fi

echo "Running gitleaks secret scan…"
set +e
docker run --rm -v "$ROOT":/src -w /src "$IMAGE" "${GITLEAKS_ARGS[@]}" \
  --report-format json --report-path "$OUT/report.json" >/dev/null 2>&1
CODE=$?
set -e

if [ "$CODE" -ne 0 ]; then
  echo "gitleaks found potential secrets (exit $CODE)."
  if [ -f "$OUT/report.json" ]; then
    python3 - "$OUT/report.json" <<'PY'
import json, sys
data = json.load(open(sys.argv[1]))
print(f"{len(data)} finding(s):")
for f in data:
    print(f"  {f.get('RuleID')}  {f.get('File')}:{f.get('StartLine')}  {f.get('Description','')}")
PY
  fi
  exit "$CODE"
fi

echo "gitleaks: no secrets found."
