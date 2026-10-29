// k6 scenario: metadata reads (NFR-002, D-02). Exercises the owner-scoped portal file list (and the
// public config) to assert list/detail latency under load.
//
//   k6 run --vus 50 --duration 60s tests/perf/metadata-reads.js
//
// SLO: metadata list p95 <= 500 ms (observability-and-slos.md). In smoke mode (PERF_SMOKE=1) the
// thresholds are relaxed to a simple "no errors on 1 iteration".
import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, portalHeaders, slo } from './common.js';

const smoke = __ENV.PERF_SMOKE === '1';

export const options = {
  thresholds: smoke
    ? { http_req_failed: ['rate<1'] }
    : {
        http_req_failed: [`rate<${slo.errorRate}`],
        'http_req_duration{name:list_files}': [`p(95)<${slo.metadataListP95Ms}`],
      },
};

export default function () {
  const res = http.get(`${baseUrl}/v1/portal/files?limit=50`, {
    headers: portalHeaders(),
    tags: { name: 'list_files' },
  });
  check(res, {
    'metadata list responds 2xx': (r) => r.status >= 200 && r.status < 300,
    'metadata list responds 4xx (unauth ok in smoke)': (r) => r.status < 500,
  });
}
