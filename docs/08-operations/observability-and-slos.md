# Observability, SLOs, and Alerting

**Stack:** OpenTelemetry (.NET + Angular) → OTel Collector → SigNoz (traces, metrics, logs).
CloudWatch remains the source for AWS-managed resource metrics and log retention.

## 1. Service-level objectives

| SLO | SLI | Target (30 days) | Error budget |
|---|---|---|---|
| API availability | Non-5xx responses / valid requests (excluding 4xx client errors) | ≥99.9% | 43.2 min |
| Portal availability | Same, for `/v1/portal/*` and SPA host | ≥99.9% | 43.2 min |
| Metadata latency | p95 of GET list/detail | ≤300 ms (detail), ≤500 ms (list) | — |
| Upload session create | p95 | ≤500 ms | — |
| Download TTFB | p95, S3 warm | ≤800 ms | — |
| Upload success | Completed uploads / started sessions (excluding 4xx) | ≥99.5% | — |
| Download success | Completed streams / authorized streams | ≥99.5% | — |
| Expiration freshness | Time since last successful expiration job | ≤30 min | — |
| Deletion freshness | Oldest `pending_deletion` (excluding retrying) | ≤1 h | — |
| Outbox freshness | `outbox_lag_seconds` p95 | ≤60 s | — |
| Notification acceptance | Accepted / due (excluding no-recipient) | ≥98% | — |
| Key lockout coverage | Locked attempts / failed attempts above threshold | 100% | — |

**Error budget policy:** if >50% of the monthly budget is consumed in 24 h, freeze risky releases
until the cause is understood. If 100% is consumed, releases require an incident review.
Availability is measured at the ALB and the application (both must agree).

## 2. Instrumentation

### Traces

- ASP.NET Core, HttpClient, EF Core/Npgsql, AWS SDK, Quartz jobs.
- Custom spans: `upload.session.create`, `upload.complete`, `s3.presign`, `s3.complete`,
  `link.redeem`, `key.verify`, `grant.issue`, `grant.redeem`, `download.stream`,
  `outbox.dispatch`, `job.run`.
- Attributes: `file.id` (allowed), `owner.type`, `integration.id`, `link.id` (never the token),
  `http.route`, `job.name`. Never: tokens, keys, object keys, filenames, email addresses.
- Sampling: parent-based 10% in prod (100% for jobs/errors via tail sampling at the collector),
  50% staging, 100% dev. `traceparent` propagated to S3? (not supported; use spans around calls).

### Metrics (OTel + Prometheus naming at SigNoz)

| Name | Type | Labels |
|---|---|---|
| `http_server_requests_total` | counter | route, method, status_class |
| `http_server_request_duration_seconds` | histogram | route, method |
| `files_uploads_started_total` | counter | owner_type, mode |
| `files_uploads_completed_total` | counter | result |
| `files_upload_bytes_total` | counter | owner_type |
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
| `reconcile_missing_db_total` | counter | — |
| `reconcile_orphan_s3_total` | counter | — |

### Logs

Structured JSON with: `timestamp`, `level`, `message`, `traceId`, `spanId`, `requestId`,
`route`, `method`, `status`, `durationMs`, `actorType`, `actorId`, `integrationId`, `fileId`
(only when useful), `job`, `errorCode`. No bodies for auth/upload/link endpoints; redaction list
in backend design. Log retention 30 days (prod), 7 days (dev/staging); audit data lives in the DB.

## 3. Dashboards

1. **API RED:** request rate, error rate by code, p50/p95/p99 latency, saturation (CPU/memory),
   top routes, 401/403/429 trends.
2. **Uploads:** sessions started/completed by mode, failures by reason, bytes, part errors, S3
   latency, open sessions.
3. **Downloads & links:** downloads by source, grant issuance/redeem, key failures/lockouts,
   reservation conflicts, egress bytes, TTFB.
4. **Jobs & outbox:** runs, durations, last success age, outbox lag, dead letters, pending
   notifications, pending deletions.
5. **Data:** DB connections/CPU/storage/IOPS, slow queries, S3 4xx/5xx, reconciliation counts.
6. **Cost:** S3 storage/egress/requests, Fargate vCPU-hours, NAT bytes, RDS instance-hours.

## 4. Alerts

| Alert | Condition | Severity | Runbook |
|---|---|---|---|
| API 5xx burn | 5xx rate >1% for 5 min (or error budget burn >14× for 1 h) | P1 | `runbooks.md#api-errors` |
| Latency | p95 >2× SLO for 10 min | P2 | `runbooks.md#latency` |
| Readiness failing | ≥1 task failing `/health/ready` for 5 min | P1 | `runbooks.md#dependency-outage` |
| S3 errors | `s3_requests_total{result="error"}` >5% for 5 min | P1 | `runbooks.md#s3-outage` |
| Deletion failures | >5 pending for 15 min, or oldest >2 h | P1 | `runbooks.md#deletion-failures` |
| Outbox lag | >60 s for 10 min | P2 | `runbooks.md#outbox-lag` |
| Job stale | last success >2× schedule | P2 | `runbooks.md#stuck-jobs` |
| Notification failures | >10 permanent failures in 1 h | P2 | `runbooks.md#email-failures` |
| Key abuse | >100 failed attempts for a link in 1 h, or lockouts spike 10× | P2/P1 | `runbooks.md#download-abuse` |
| Auth anomalies | integration auth failures >50/min | P2 | `runbooks.md#credential-issues` |
| DB capacity | storage <10 GiB, CPU >80% 10 min, connections >80% | P1/P2 | `runbooks.md#database` |
| Certificate expiry | <30 days | P2 | `runbooks.md#certificates` |
| Cost anomaly | Budget 80/100/120% or anomaly >20% | P2 | `runbooks.md#cost-anomaly` |
| Reconciliation drift | missing DB objects >0 | P1 | `runbooks.md#reconciliation` |

Alerts route to the on-call channel with a link to the runbook and the dashboard; P1 also pages.
No alert without an action and a runbook.

## 5. Collector configuration (skeleton)

```yaml
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
      http: { endpoint: 0.0.0.0:4318 }
processors:
  batch: { timeout: 5s }
  memory_limiter: { check_interval: 1s, limit_mib: 512 }
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

## 6. Health endpoints

- `/health/live`: process alive; no dependency checks; used by ECS container health.
- `/health/ready`: DB reachable and migrations current; S3 bucket reachable (cached 30 s);
  returns 503 with check details (no secrets) when not ready.
- `/health/startup`: optional for slow migrations; not used if migrations run separately.

## 7. Operational review cadence

- Daily: error budget, denied-download spikes, pending deletions, failed notifications.
- Weekly: SLO trend, top error codes, cost vs budget, dependency CVEs.
- Monthly: capacity forecast (storage/egress), access review, alert noise review.
- Quarterly: DR restore drill, threat-model refresh, retention/backup verification.
