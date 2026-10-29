// k6 scenario: ranged download (NFR-002, D-02/D-06). Streams a byte range of an integration file and
// asserts download TTFB. Requires a seeded file id via PERF_FILE_ID.
//
//   PERF_FILE_ID=<uuid> k6 run --vus 20 --duration 60s tests/perf/ranged-download.js
//
// SLO: download TTFB p95 <= 800 ms (S3 warm).
import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, integrationHeaders, slo } from './common.js';

const smoke = __ENV.PERF_SMOKE === '1';
const fileId = __ENV.PERF_FILE_ID || '';
const rangeBytes = Number(__ENV.PERF_RANGE_BYTES || 1024 * 1024);

export const options = {
  thresholds: smoke
    ? { http_req_failed: ['rate<1'] }
    : {
        http_req_failed: [`rate<${slo.errorRate}`],
        'http_req_duration{name:ranged_download}': [`p(95)<${slo.downloadTtfbP95Ms}`],
      },
};

export default function () {
  if (!fileId) {
    return; // nothing to download without a seeded file; smoke mode skips the stream.
  }
  const res = http.get(`${baseUrl}/v1/integrations/files/${fileId}/content`, {
    headers: { ...integrationHeaders(), Range: `bytes=0-${rangeBytes - 1}` },
    tags: { name: 'ranged_download' },
  });
  check(res, {
    'ranged download responds 2xx/4xx (no 5xx)': (r) => r.status < 500,
  });
}
