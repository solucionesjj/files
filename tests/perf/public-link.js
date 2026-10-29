// k6 scenario: public-link redemption and abuse (D-02/D-03, FR-LINK-009). Redeems a public link
// with a valid key (issued once) and hammers it with a wrong key to assert the throttle/lockout
// path stays fast and returns 4xx (never 5xx). Requires PERF_LINK_TOKEN + PERF_LINK_KEY.
//
//   PERF_LINK_TOKEN=<token> PERF_LINK_KEY=<8char> k6 run --vus 20 --duration 60s tests/perf/public-link.js
import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, slo } from './common.js';

const smoke = __ENV.PERF_SMOKE === '1';
const token = __ENV.PERF_LINK_TOKEN || '';
const key = __ENV.PERF_LINK_KEY || '';

export const options = {
  thresholds: smoke
    ? { http_req_failed: ['rate<1'] }
    : {
        http_req_failed: [`rate<${slo.errorRate}`],
        // Locked-out key attempts must add <5 ms (KeyThrottle skips hashing when locked).
        'http_req_duration{name:key_attempt}': ['p(99)<5'],
      },
};

export default function () {
  if (!token) {
    return;
  }
  // Alternate: one correct key (201 grant) and nine wrong keys (404/429) per iteration.
  const useCorrectKey = (__ITER % 10) === 0 && key !== '';
  const attempt = http.post(
    `${baseUrl}/v1/public/download-links/${token}/downloads`,
    JSON.stringify({ key: useCorrectKey ? key : 'ZZZZZZZZ' }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'key_attempt' } },
  );
  check(attempt, {
    'key attempt never returns 5xx': (r) => r.status < 500,
  });
}
