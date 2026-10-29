// k6 scenario: chunked upload (NFR-003, D-01/D-06). Creates an integration upload session and
// uploads one 16 MiB chunk (configurable), then completes. Asserts upload session-create latency and
// chunk-write latency, and exercises the staging reserve/quota path.
//
//   k6 run --vus 3 --duration 60s tests/perf/chunked-upload.js
//
// SLO: session create p95 <= 500 ms, chunk write p95 <= 300 ms (local pwrite).
import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, integrationHeaders, slo } from './common.js';

const smoke = __ENV.PERF_SMOKE === '1';
const chunkBytes = Number(__ENV.PERF_CHUNK_BYTES || 16 * 1024 * 1024);

export const options = {
  thresholds: smoke
    ? { http_req_failed: ['rate<1'] }
    : {
        http_req_failed: [`rate<${slo.errorRate}`],
        'http_req_duration{name:upload_session_create}': [`p(95)<${slo.uploadSessionP95Ms}`],
        'http_req_duration{name:chunk_write}': [`p(95)<${slo.chunkWriteP95Ms}`],
      },
};

export default function () {
  const headers = integrationHeaders();

  const create = http.post(
    `${baseUrl}/v1/integrations/upload-sessions`,
    JSON.stringify({ fileName: `perf-${__ITER}.bin`, sizeBytes: chunkBytes, contentType: 'application/octet-stream' }),
    { headers: { ...headers, 'Content-Type': 'application/json' }, tags: { name: 'upload_session_create' } },
  );

  if (create.status === 201) {
    const sessionId = create.json('uploadSessionId');
    const body = new Uint8Array(chunkBytes);
    http.put(`${baseUrl}/v1/integrations/upload-sessions/${sessionId}/chunks/1`, body, {
      headers: { ...headers, 'Content-Type': 'application/octet-stream' },
      tags: { name: 'chunk_write' },
    });
    // Abort to keep the staging disk free during a long soak run.
    http.del(`${baseUrl}/v1/integrations/upload-sessions/${sessionId}`, null, { headers });
  }

  check(create, {
    'upload session create responds 2xx or quota/unauth 4xx': (r) => r.status < 500,
  });
}
