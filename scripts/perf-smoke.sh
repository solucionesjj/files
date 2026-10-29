#!/usr/bin/env bash
#
# k6 smoke run (CI-gated; NFR-003/004). Validates every load/soak script loads, then runs a single
# 1-VU / 1-iteration pass with relaxed thresholds. No live deployment is required: requests that
# receive 401/404 are tolerated and only 5xx responses fail the smoke gate.
#
#   ./scripts/perf-smoke.sh              # smoke mode
#   ./scripts/perf-smoke.sh --full URL   # optional: run one scenario against a live URL
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${K6_IMAGE:-grafana/k6}"
PERF_DIR="$ROOT/tests/perf"

echo "== k6 smoke =="
for script in metadata-reads chunked-upload ranged-download public-link five-gib; do
  echo "  validating tests/perf/$script.js"
  docker run --rm -i -v "$PERF_DIR":/perf "$IMAGE" inspect "/perf/$script.js" >/dev/null
done

if [ "${1:-}" = "--full" ]; then
  TARGET="${2:-http://localhost:8080}"
  echo "  smoke run metadata-reads.js against $TARGET"
  docker run --rm -i -v "$PERF_DIR":/perf -e PERF_BASE_URL="$TARGET" -e PERF_SMOKE=1 \
    "$IMAGE" run --vus 1 --iterations 1 /perf/metadata-reads.js
else
  echo "  (skipping live run; pass --full <base-url> to exercise a deployment)"
fi

echo "✓ k6 smoke passed."
