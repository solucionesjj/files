# Observability, SLOs, and Alerting (Single EC2)

**Stack:** OpenTelemetry (.NET) → OTel Collector → SigNoz (self-hosted or external). Host and
container metrics come from Docker/systemd; there is no CloudWatch or ALB in this phase.

## 1. Service-level objectives

| SLO | SLI | Target | Notes |
|---|---|---|---|
| Availability | Non-5xx responses / valid requests | ≥99.5% monthly, excluding announced maintenance | Single host; best effort. |
| Metadata latency | p95 GET list/detail | ≤300 ms detail, ≤500 ms list | Warm cache/DB. |
| Upload session create | p95 | ≤500 ms | Disk check included. |
| Chunk write | p95 | ≤300 ms for 16 MiB (network-bound) | Local pwrite. |
| Download TTFB | p95 | ≤800 ms | S3 warm. |
| Upload success | Completed / started sessions (excl. 4xx) | ≥99.5% | S3 commit retries excluded from failure? count final. |
| Download success | Completed streams / authorized | ≥99.5% | — |
| Expiration freshness | Time since last successful job | ≤30 min | — |
| Deletion freshness | Oldest `pending_deletion` | ≤1 h normal | Retries may extend; alert. |
| Outbox freshness | p95 lag | ≤60 s | — |
| Notification acceptance | Accepted / due | ≥98% | Graph. |
| Backup freshness | Latest valid dump age | ≤26 h | Nightly cron. |
| Disk headroom | Free bytes above reserve | >10 GiB | Hard fail-closed at reserve. |

**Error budget:** 99.5% ≈ 3.6 h/month. If >50% is consumed in 24 h, freeze risky deploys until
understood. All maintenance windows are announced and excluded only if declared in advance.

## 2. Instrumentation

### Traces

- ASP.NET Core, HttpClient, EF Core/Npgsql, AWS SDK, Quartz jobs.
- Custom spans: `upload.session.create`, `chunk.write`, `upload.verify`, `s3.commit`, `s3.head`,
  `link.redeem`, `key.verify`, `grant.issue`, `grant.redeem`, `download.stream`,
  `outbox.dispatch`, `job.run`.
- Attributes: `file.id` (allowed), `owner.type`, `integration.id`, `link.id` (never the token),
  `http.route`, `job.name`. Never tokens, keys, object keys, filenames, emails, chunk bodies.
- Sampling: 10% parent-based in production (100% for jobs/errors if tail sampling is available),
  50% staging, 100% dev. `traceparent` propagation is internal.

### Metrics

| Name | Type | Labels |
|---|---|---|
| `http_server_requests_total` | counter | route, method, status_class |
| `http_server_request_duration_seconds` | histogram | route, method |
| `files_uploads_started_total` | counter | owner_type |
| `files_uploads_completed_total` | counter | result |
| `files_upload_bytes_total` | counter | owner_type |
| `upload_chunks_total` | counter | result |
| `upload_staging_bytes` | gauge | — |
| `clamav_scans_total` | counter | result |
| `clamav_scan_duration_seconds` | histogram | — |
| `clamav_up` | gauge | — |
| `staging_disk_free_bytes` | gauge | — |
| `files_downloads_total` | counter | source, result |
| `files_download_bytes_total` | counter | source |
| `files_expired_total` | counter | — |
| `files_deleted_total` | counter | — |
| `files_deletion_failures_total` | counter | reason |
| `files_pending_deletion` | gauge | — |
| `download_completion_lost_total` | counter | source |
| `link_key_attempts_total` | counter | result |
| `link_lockouts_total` | counter | scope |
| `link_grants_total` | counter | source |
| `link_reservation_conflicts_total` | counter | — |
| `notifications_scheduled_total` | counter | interval_days |
| `notifications_sent_total` | counter | status |
| `notification_delivery_failures_total` | counter | reason |
| `job_runs_total` | counter | job, result |
| `job_duration_seconds` | histogram | job |
| `job_last_success_timestamp_seconds` | gauge | job |
| `outbox_lag_seconds` | gauge | — |
| `outbox_dead_total` | counter | — |
| `s3_requests_total` | counter | operation, result |
| `s3_request_duration_seconds` | histogram | operation |
| `db_pool_connections` | gauge | state |
| `integration_auth_failures_total` | counter | reason |
| `host_cpu_percent`, `host_memory_used_bytes`, `host_disk_free_bytes` | gauge | mount |
| `container_restarts_total` | counter | service |
| `backup_last_success_timestamp_seconds` | gauge | — |
| `reconcile_missing_db_total` | counter | — |
| `reconcile_orphan_s3_total` | counter | — |

### Logs

Structured JSON: `timestamp`, `level`, `message`, `traceId`, `spanId`, `requestId`, `route`,
`method`, `status`, `durationMs`, `actorType`, `actorId`, `integrationId`, `fileId` (when safe),
`job`, `errorCode`. No bodies for auth/upload/link endpoints. Docker log rotation 10 MB × 3.
Caddy access logs are disabled for token-bearing URIs.

## 3. Dashboards

1. **Edge/API RED:** requests, 5xx rate, p95/p99, 401/403/429, TLS/host status.
2. **Uploads:** sessions, chunks, staging bytes/free disk, S3 commit failures, session cleanup.
3. **Downloads & links:** source mix, grant issuance/redeem, key failures/lockouts, egress.
4. **Jobs/outbox:** runs, durations, last success, lag, pending deletions/notifications, backup
   freshness.
5. **Host:** CPU, memory, swap, disk (staging + PostgreSQL), network, container restarts.
6. **Cost/usage:** S3 storage/egress/requests, EC2 hours, data transfer.

## 4. Alerts

| Alert | Condition | Severity | Runbook |
|---|---|---|---|
| Host down | External HTTPS health fails 3× (5 min) | P1 | `runbooks.md#host-unreachable` |
| API 5xx | >1% for 5 min | P1 | `runbooks.md#api-errors` |
| Disk critical | Free < reserve (10 GiB) or >90% | P1 | `runbooks.md#disk-pressure` |
| Disk warning | >80% | P2 | `runbooks.md#disk-pressure` |
| Staging writes failing | `upload_staging` errors >5/min | P1 | `runbooks.md#disk-pressure` |
| Container restart loop | >3 restarts/15 min | P1 | `runbooks.md#container-down` |
| S3 errors | >5% for 5 min | P1 | `runbooks.md#s3-outage` |
| Deletion failures | >5 pending for 15 min or oldest >2 h | P1 | `runbooks.md#deletion-failures` |
| Outbox lag | >60 s for 10 min | P2 | `runbooks.md#outbox-lag` |
| Job stale | last success >2× schedule | P2 | `runbooks.md#outbox-lag` |
| Notification failures | >10 permanent failures in 1 h | P2 | `runbooks.md#email-failures` |
| ClamAV down/failing | `clamav_up=0` or scan failures >3 in 15 min | P1 | `runbooks.md#clamav-failures` |
| Files stuck processing | >10 processing >30 min | P2 | `runbooks.md#clamav-failures` |
| Key abuse | >100 failures/link/hour or lockouts spike | P2/P1 | `runbooks.md#download-abuse` |
| Backup missing/stale | >26 h | P1 | `runbooks.md#backup-failure` |
| TLS certificate | <14 days or renewal failure | P1 | `runbooks.md#tls` |
| Cost anomaly | Budget 80/100/120% or egress spike | P2 | `runbooks.md#cost-anomaly` |
| Reconciliation drift | missing DB objects >0 | P1 | `runbooks.md#reconciliation` |

Alerts route to the on-call channel with the runbook and dashboard. No alert without an action.

## 5. Collector configuration (skeleton)

```yaml
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
      http: { endpoint: 0.0.0.0:4318 }
processors:
  batch: { timeout: 5s }
  memory_limiter: { check_interval: 1s, limit_mib: 256 }
  attributes/redact:
    actions:
      - { key: http.request.header.authorization, action: delete }
      - { key: http.request.header.cookie, action: delete }
      - { key: url.query, action: delete }
exporters:
  otlp/signoz:
    endpoint: ${env:SIGNOZ_OTLP_ENDPOINT}
    tls: { insecure: false }
service:
  pipelines:
    traces: { receivers: [otlp], processors: [memory_limiter, batch], exporters: [otlp/signoz] }
    metrics: { receivers: [otlp], processors: [memory_limiter, batch], exporters: [otlp/signoz] }
    logs: { receivers: [otlp], processors: [memory_limiter, batch], exporters: [otlp/signoz] }
```

On a `t3.large`, self-hosting SigNoz may not fit alongside PostgreSQL/API/ClamAV. Prefer an external
SigNoz instance or reduce retention/sampling.

## 6. Health endpoints

- `/health/live`: process alive; no dependency checks; used by Docker/Caddy.
- `/health/ready`: DB reachable, migrations current, staging directory writable with reserve,
  S3 bucket reachable (cached 30 s). Returns 503 with non-sensitive check names otherwise.

## 7. Operational review cadence

- Daily: error budget, disk/staging, pending deletions, failed notifications, backup marker.
- Weekly: SLO trend, top error codes, cost vs budget, dependency CVEs, TLS expiry.
- Monthly: capacity (disk/S3/egress), access review, alert noise, patch status.
- Quarterly: restore drill, threat-model refresh, retention/backup verification.
