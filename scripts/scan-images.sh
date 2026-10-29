#!/usr/bin/env bash
#
# Container image scanning (E-04 / NFR-009) via Trivy. Builds the API and worker images (unless
# SKIP_BUILD=1) then scans them for HIGH/CRITICAL vulnerabilities and misconfigurations.
#
#   ./scripts/scan-images.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${TRIVY_IMAGE:-aquasec/trivy:latest}"
OUT="$ROOT/artifacts/trivy"
mkdir -p "$OUT"

if [ "${SKIP_BUILD:-0}" != "1" ]; then
  echo "Building API/worker images…"
  docker build -f src/Files.Api/Dockerfile -t files-api:local "$ROOT"
  docker build -f src/Files.Worker/Dockerfile -t files-worker:local "$ROOT"
fi

for target in files-api:local files-worker:local; do
  slug="${target%%:*}"
  echo "Scanning $target…"
  docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$ROOT":/src "$IMAGE" image \
    --scanners vuln,misconfig \
    --severity HIGH,CRITICAL \
    --format json --output "/src/artifacts/trivy/${slug}.json" \
    "$target"
done

echo "Trivy image reports written to artifacts/trivy/."
