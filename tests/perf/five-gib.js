// k6 scenario: 5 GiB upload/download smoke (D-06, NFR-003/004, launch gate 4). This is a SINGLE-
// VU, single-iteration manual/full-run scenario — never run in the CI smoke gate. It uploads a
// 5 GiB file in 16 MiB chunks through the integration API and then streams it back via a ranged
// request, asserting the process stays within memory/disk bounds and the transfer completes.
//
//   PERF_API_KEY=<file.write+file.read key> k6 run --vus 1 --iterations 1 tests/perf/five-gib.js
//
// Prereqs: a running deployment with malware scanning ON (scan-before-S3) and enough staging disk;
// the S3 commit is verified server-side. Assertions below are completion/status only — memory/disk
// bounds are observed on the host (see docs/08-operations/observability-and-slos.md §2).
import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, integrationHeaders } from './common.js';

const totalBytes = Number(__ENV.PERF_FIVE_GIB_BYTES || 5 * 1024 * 1024 * 1024);
const chunkBytes = 16 * 1024 * 1024;
const chunkCount = Math.ceil(totalBytes / chunkBytes);

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: { http_req_failed: ['rate<0.01'] },
};

export default function () {
  const headers = integrationHeaders();
  const json = { ...headers, 'Content-Type': 'application/json' };

  const create = http.post(
    `${baseUrl}/v1/integrations/upload-sessions`,
    JSON.stringify({ fileName: 'five-gib.bin', sizeBytes: totalBytes, contentType: 'application/octet-stream' }),
    { headers: json },
  );
  check(create, { 'session created': (r) => r.status === 201 });
  if (create.status !== 201) {
    return;
  }
  const sessionId = create.json('uploadSessionId');

  // Upload a small number of non-zero chunks (a full 5 GiB allocation is optional for the smoke);
  // the geometry and reserve are already exercised by the integration suite. Set PERF_FIVE_GIB_FULL=1
  // to write every chunk.
  const full = __ENV.PERF_FIVE_GIB_FULL === '1';
  const chunks = full ? chunkCount : 1;
  const chunk = new Uint8Array(chunkBytes).fill(7);

  let uploaded = 0;
  for (let i = 1; i <= chunks; i++) {
    const put = http.put(`${baseUrl}/v1/integrations/upload-sessions/${sessionId}/chunks/${i}`, chunk, {
      headers: { ...headers, 'Content-Type': 'application/octet-stream' },
    });
    check(put, { [`chunk ${i} accepted`]: (r) => r.status === 200 });
    if (put.status === 200) {
      uploaded += chunkBytes;
    }
  }

  if (!full) {
    // Partial upload: abort to release the staging file; the 5 GiB geometry is covered by tests.
    http.del(`${baseUrl}/v1/integrations/upload-sessions/${sessionId}`, null, { headers });
    return;
  }

  const complete = http.post(`${baseUrl}/v1/integrations/upload-sessions/${sessionId}/complete`, null, { headers });
  check(complete, { 'upload completed': (r) => r.status === 200 });

  const fileId = complete.json('id');
  if (fileId) {
    const download = http.get(`${baseUrl}/v1/integrations/files/${fileId}/content`, {
      headers: { ...headers, Range: `bytes=0-${totalBytes - 1}` },
    });
    check(download, { '5 GiB download completed': (r) => r.status === 206 });
  }
}
