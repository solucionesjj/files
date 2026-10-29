# Load / soak testing (k6)

Scenarios for NFR-002/003/004 and threat-model D-01/D-02/D-06. They run against any deployment
via `PERF_BASE_URL` plus a credential, and encode the SLO thresholds from
`docs/08-operations/observability-and-slos.md`.

## Scripts

| Script | Purpose | SLO asserted |
|---|---|---|
| `metadata-reads.js` | Owner-scoped portal list under load | list p95 ≤ 500 ms |
| `chunked-upload.js` | Integration upload session + chunk write | create p95 ≤ 500 ms, chunk p95 ≤ 300 ms |
| `ranged-download.js` | Integration ranged content stream | TTFB p95 ≤ 800 ms |
| `public-link.js` | Key redemption + wrong-key abuse/lockout | key attempt p99 < 5 ms when locked |
| `five-gib.js` | 5 GiB upload/download smoke (manual) | completion only |

## Environment

| Variable | Meaning |
|---|---|
| `PERF_BASE_URL` | API base (default `http://localhost:8080`). |
| `PERF_BEARER` | Human Entra access token for portal endpoints. |
| `PERF_API_KEY` | Files-issued integration key (scopes as needed). |
| `PERF_FILE_ID` | Seeded integration file id for `ranged-download.js`. |
| `PERF_LINK_TOKEN` / `PERF_LINK_KEY` | Public link token + 8-char key (shown once at creation). |
| `PERF_SMOKE` | `1` = relaxed thresholds (CI smoke), unset = enforce SLOs. |

## CI smoke mode

`make perf-smoke` (or `./scripts/perf-smoke.sh`) validates every script loads and runs a 1-VU,
1-iteration pass with relaxed thresholds — no live deployment is required (it tolerates 401/404 and
only fails on a 5xx or a script syntax error).

## Full run (pre-launch)

```bash
docker compose up -d                      # local stack (PostgreSQL 18, LocalStack S3, Mailpit, OTel)
# seed a user/integration + a file, capture a Bearer token and an X-Api-Key

# metadata reads: 50 VUs for 5 minutes
PERF_BASE_URL=http://localhost:8080 PERF_BEARER=$TOKEN \
  docker run --rm -i -v "$PWD/tests/perf:/perf" grafana/k6 run --vus 50 --duration 5m /perf/metadata-reads.js

# chunked upload: 3 concurrent sessions for 5 minutes
PERF_BASE_URL=http://localhost:8080 PERF_API_KEY=$KEY \
  docker run --rm -i -v "$PWD/tests/perf:/perf" grafana/k6 run --vus 3 --duration 5m /perf/chunked-upload.js

# ranged download: 20 concurrent streams for 5 minutes
PERF_BASE_URL=http://localhost:8080 PERF_API_KEY=$KEY PERF_FILE_ID=$FILE_ID \
  docker run --rm -i -v "$PWD/tests/perf:/perf" grafana/k6 run --vus 20 --duration 5m /perf/ranged-download.js

# public-link abuse: 20 VUs for 5 minutes
PERF_BASE_URL=http://localhost:8080 PERF_LINK_TOKEN=$TOKEN PERF_LINK_KEY=$KEY \
  docker run --rm -i -v "$PWD/tests/perf:/perf" grafana/k6 run --vus 20 --duration 5m /perf/public-link.js

# 5 GiB smoke: single VU, single iteration (set PERF_FIVE_GIB_FULL=1 for every chunk)
PERF_BASE_URL=http://localhost:8080 PERF_API_KEY=$KEY \
  docker run --rm -i -v "$PWD/tests/perf:/perf" grafana/k6 run --vus 1 --iterations 1 /perf/five-gib.js
```

A pre-launch soak runs these at 2× baseline for ≥2 hours while the host CPU/memory/disk and the
SLO dashboards are watched (launch gate 4, testing-strategy §8).
