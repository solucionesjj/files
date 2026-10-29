// Shared k6 helpers and targets for the Files load/soak suite (NFR-002/003/004, D-01/D-02/D-06).
// The base URL and credentials come from environment variables so the same scripts run against a
// local compose, a disposable host, or staging without code changes.
export const baseUrl = (__ENV.PERF_BASE_URL || 'http://localhost:8080').replace(/\/$/, '');

// Human (Bearer) and machine (X-Api-Key) credentials. In CI smoke mode these may be empty; the
// smoke runner only exercises endpoints that tolerate missing credentials (e.g. health + 401s).
export const bearer = __ENV.PERF_BEARER || '';
export const apiKey = __ENV.PERF_API_KEY || '';

export function portalHeaders() {
  return bearer ? { Authorization: `Bearer ${bearer}` } : {};
}

export function integrationHeaders() {
  return apiKey ? { 'X-Api-Key': apiKey } : {};
}

// SLO thresholds from docs/08-operations/observability-and-slos.md. A "full" run fails the CI gate
// when a budget is exceeded; the smoke run uses relaxed thresholds so it only verifies wiring.
export const slo = {
  metadataListP95Ms: 500,
  chunkWriteP95Ms: 300,
  downloadTtfbP95Ms: 800,
  uploadSessionP95Ms: 500,
  errorRate: 0.005, // 99.5% availability
};
