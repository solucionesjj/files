# Observability: dashboards, alerts, and on-call (launch gate 6)

Concrete dashboard and alert definitions for the SigNoz stack, wired to the API/worker OTLP
telemetry plus host/container metrics collected by the `otel-collector` (see
[`otel-collector.yaml`](../otel-collector.yaml)). Every alert maps to a runbook action in
[`docs/08-operations/runbooks.md`](../../docs/08-operations/runbooks.md) and an on-call route below.
SLO targets are in [`docs/08-operations/observability-and-slos.md`](../../docs/08-operations/observability-and-slos.md).

- [`signoz-alerts.yaml`](signoz-alerts.yaml) — PromQL alert rules (severity, runbook, dashboard).
- [`signoz-dashboards.yaml`](signoz-dashboards.yaml) — six dashboards with panel queries.

## 1. Wire-up (one-time)

1. Provision an external SigNoz instance (a `t3.large` cannot self-host SigNoz alongside
   PostgreSQL/API/ClamAV — see `observability-and-slos.md` §5).
2. Set `SIGNOZ_OTLP_ENDPOINT` in `/opt/files/.env` (e.g. `https://ingest.<signoz-host>:443`).
3. Start the collector:
   `docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml --profile observability up -d otel-collector`.
   It exports OTLP traces/metrics/logs from the API/worker **and** collects host metrics
   (`hostmetrics` receiver) and container metrics (`docker_stats` receiver, read-only Docker socket).
4. In SigNoz: **Settings → Alert Channels** → create the on-call channel (below). Then for each rule
   in [`signoz-alerts.yaml`](signoz-alerts.yaml) create a **PromQL** alert with the same `expr`,
   `for`, severity label, and the runbook/dashboard annotations.

## 2. On-call route

| Severity | Routing | Action |
|---|---|---|
| P1 | Page the on-call engineer (PagerDuty/Opsgenie, `files-oncall` escalation policy) | Acknowledge < 15 min, run the runbook, roll back if a deploy correlates |
| P2 | Notify `#files-ops` (Slack/Teams) + on-call queue | Triage within business hours |

- Rota: `files-oncall` weekly rotation; secondary is the infrastructure owner (host/S3 are managed
  outside this repo). The primary handles app-level incidents; host/S3/bucket-policy issues escalate
  to the infra owner (runbooks §Host unreachable / §S3 outage).
- No alert without an action: every alert's `runbook` annotation points at the exact runbook section.

## 3. Alert → runbook → action map

| Alert | Severity | Runbook | Immediate action |
|---|---|---|---|
| HostDown | P1 | `runbooks.md#host-unreachable` | Reboot/stop-start; replace host + restore + repoint DNS |
| ApiHigh5xxRate | P1 | `runbooks.md#api-errors` | Check deps/logs/disk; roll back if post-deploy |
| DiskCritical / DiskWarning | P1/P2 | `runbooks.md#disk-pressure` | Free staging/WAL; grow the volume with infra owner |
| StagingWritesFailing | P1 | `runbooks.md#disk-pressure` | Check disk reserve + cleanup job |
| ContainerRestartLoop | P1 | `runbooks.md#container-down` | `docker compose ps`/`config`; restart or roll back |
| S3Errors | P1 | `runbooks.md#s3-outage` | Verify static key + bucket policy (escalate to infra owner) |
| DeletionFailures | P1 | `runbooks.md#deletion-failures` | Fix cause; `UPDATE files SET next_deletion_at=now()` |
| OutboxLag | P2 | `runbooks.md#outbox-lag` | Restart worker; reset dead letters |
| JobStale | P2 | `runbooks.md#outbox-lag` | Verify `job_last_success` advances |
| NotificationFailures | P2 | `runbooks.md#email-failures` | Verify SMTP AUTH/credentials; reset `failed` after fix |
| ClamAvDown | P1 | `runbooks.md#clamav-failures` | Check clamd memory/signatures; wait for health |
| FilesStuckProcessing | P2 | `runbooks.md#clamav-failures` | Restart worker/ClamAV |
| KeyAbuse | P2/P1 | `runbooks.md#download-abuse` | Revoke link / disable public links |
| BackupMissingOrStale | P1 | `runbooks.md#backup-failure` | Run `backup.sh`; verify `_latest`; restore into fresh DB |
| TlsExpiringSoon | P1 | `runbooks.md#tls` | Check Caddy ACME/DNS/80-443 |
| ReconciliationDrift | P1 | `runbooks.md#reconciliation` | Investigate missing objects; inform owner |
| CostAnomaly (80/100/120%) | P2 | `runbooks.md#cost-anomaly` + [`cost-model.md`](../../docs/08-operations/cost-model.md) §8 | Identify EC2/EBS/S3/egress; revoke abusive links; resize |

## 4. Metric sources

| Source | Collector | Metrics |
|---|---|---|
| Files API/worker | OTLP (gRPC/HTTP) | `http_server_*`, `files_*`, `clamav_*`, `upload_*`, `link_*`, `notification_*`, `job_*`, `outbox_*`, `s3_requests_*`, `reconcile_*` |
| Host | `hostmetrics` receiver | `system_cpu_utilization`, `system_memory_usage`, `system_filesystem_usage`, `system_network_io` |
| Containers | `docker_stats` receiver | `container_restarts_total`, `container_cpu_usage_seconds_total`, `container_memory_usage` |
| External HTTPS / TLS | synthetic monitor (external) | `probe_success{target="files.sacservices.cloud"}`, `cert_expiry_seconds` |

`backup_last_success_timestamp_seconds` is emitted by a small host-side helper (documented in
[`runbooks.md` §Backup failure](../../docs/08-operations/runbooks.md)); until a dedicated exporter is
installed, the backup-freshness alert can equivalently read the `_latest` marker age from the
nightly backup job's exit status.

## 5. SLO / error-budget

- Availability SLO ≥99.5% (≈3.6 h/month). If the **Error budget burn** panel exceeds 50% of the
  monthly budget in 24 h, freeze risky deploys until understood (`observability-and-slos.md` §1).
- Weekly review: SLO trend, top error codes, cost vs budget, dependency CVEs, TLS expiry.
