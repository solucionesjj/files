#!/usr/bin/env bash
#
# OWASP ZAP baseline DAST pass (S-04 / NFR-009), run inside Docker. Point it at a running
# local/staging-like deployment of the API and portal. The report is written under artifacts/zap/.
#
#   ./scripts/zap-baseline.sh                       # scans $ZAP_TARGET_URL (default http://localhost:8080)
#   ZAP_TARGET_URL=http://localhost:4200 ./scripts/zap-baseline.sh
#
# The baseline pass records findings without failing on warnings (-I); before launch, review
# artifacts/zap/report.html and remediate every high-severity alert, then re-run an authenticated
# active pass (see docs/07-quality/security-scanning.md).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${ZAP_IMAGE:-owasp/zap2docker-stable}"
TARGET="${ZAP_TARGET_URL:-http://localhost:8080}"
OUT="$ROOT/artifacts/zap"
mkdir -p "$OUT"

echo "Running OWASP ZAP baseline against $TARGET …"
docker run --rm -v "$OUT":/zap/wrk -t "$IMAGE" zap-baseline.py \
  -t "$TARGET" \
  -I \
  -J /zap/wrk/report.json \
  -r /zap/wrk/report.html \
  -w /zap/wrk/report.md

echo "ZAP report written to artifacts/zap/report.{json,html,md}"
