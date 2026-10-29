#!/usr/bin/env bash
#
# Dependency + IaC scanning (E-04 / NFR-009) via Trivy, run inside Docker. Scans the repository
# filesystem for vulnerable dependencies (npm/package-lock.json, .csproj, Dockerfiles) and secrets.
#
#   ./scripts/scan-deps.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${TRIVY_IMAGE:-aquasec/trivy:latest}"
OUT="$ROOT/artifacts/trivy"
mkdir -p "$OUT"

echo "Running Trivy filesystem scan (vuln + secret)…"
docker run --rm -v "$ROOT":/src -w /src "$IMAGE" fs \
  --scanners vuln,secret \
  --severity HIGH,CRITICAL \
  --format json --output /src/artifacts/trivy/fs.json \
  /src

echo "Trivy report written to artifacts/trivy/fs.json"
if python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); print(sum(len(r.get("Vulnerabilities") or []) for r in (d.get("Results") or [])))' "$OUT/fs.json" | grep -qv '^0$'; then
  echo "⚠ HIGH/CRITICAL findings present — review artifacts/trivy/fs.json."
  exit 1
fi

echo "Trivy: no HIGH/CRITICAL findings."
