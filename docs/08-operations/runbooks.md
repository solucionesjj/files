# Operational Runbooks (Single EC2)

Each runbook: symptoms → impact → immediate actions → diagnosis → remediation → verification.
Host commands assume `cd /opt/files/app` and
`DC="docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml"`.
Never paste secrets into a shell that logs history; use the `.env` file and
`docker compose config` to validate.

## First response

1. Check the external HTTPS health check, `docker compose ps`, and disk (`df -h`).
2. Check recent deploys: `/opt/files/REVISION`, `git log -1`, image tags.
3. Scope: all users, one integration, one link/file, uploads only, downloads only.
4. If a deploy correlates, roll back the containers before deep diagnosis
   ([deployment-and-rollback.md](deployment-and-rollback.md)).
5. Open an incident, assign a commander, record the timeline; preserve `docker logs` and the
   pre-deploy backup name.

---

## Host unreachable

**Symptoms:** external health check fails, SSH times out, no metrics.

1. AWS console/CLI: instance status checks, system log, EBS status.
2. If the instance is running but unresponsive: reboot once (`aws ec2 reboot-instances`); if it
   fails, stop/start (Elastic IP stays associated).
3. If the instance is impaired or the volume is damaged, create a replacement from Terraform,
   attach/restore the EBS/data (or restore the S3 `pg_dump`), redeploy, and point DNS/EIP.
4. Verify: health 200, SPA loads, upload/download smoke, backup marker fresh.

## API errors

1. `docker compose logs --tail=200 api` and the SigNoz 5xx dashboard.
2. Check dependencies: `pg_isready` inside the postgres container, S3 access (see §5), Entra reachable.
3. Check disk (`df -h /var/lib/files`), memory (`free -h`), container restarts.
4. If the error follows a deploy, roll back. If the DB is corrupt/unavailable, see §10.
5. Verify: 5xx <0.1% for 15 min, smoke tests pass.

## Disk pressure

**Symptoms:** `507 staging_capacity_exceeded`, `staging_write_failed`, disk >80/90%, PostgreSQL
errors.

1. `df -h /var/lib/files`; `du -sh /var/lib/files/*`.
2. Staging: list stale sessions/files; the cleanup job runs every 10 min. If it is not running,
   stop the worker, remove files older than 24 h whose sessions are terminal, restart.
3. PostgreSQL: check WAL/table growth, outbox/events, vacuum; do not delete DB files.
4. Resize the EBS volume (gp3) and grow the filesystem (`growpart` + `resize2fs`) if needed.
5. Verify: free space > reserve, uploads succeed, DB healthy.

## Container down

1. `docker compose ps`; `docker compose logs --tail=100 <service>`.
2. Common causes: missing/invalid `.env`, DB not ready, port conflict, OOM, bad image.
3. Validate config: `docker compose -f deploy/docker-compose.prod.yml config`.
4. Restart a single service; if the API is crash-looping after a deploy, roll back.
5. Verify: `docker compose ps` healthy, health endpoints 200.

## S3 outage

1. Confirm AWS S3 health; check `s3_requests_total{result="error"}` and API logs (no keys logged).
2. Verify the instance role is attached and the bucket policy still allows the role ARN:
   `aws sts get-caller-identity` on the host (instance profile).
3. Check bucket policy/BPA/SSE changes, bucket name/region in `.env`, clock skew, and outbound 443.
4. Do not add static keys; fix IAM/bucket policy via Terraform.
5. If S3 is down, uploads fail closed (`502 upload_commit_failed`); staged files remain and
   `complete` can be retried. Downloads fail; metadata remains available.
6. Verify: a test PUT/GET in `f/` via the API succeeds; `head-object` works with the instance role.

## Deletion failures

```sql
SELECT id, owner_type, deletion_attempts, next_deletion_at, last_deletion_error
FROM files WHERE status = 'pending_deletion' ORDER BY next_deletion_at LIMIT 50;
```

1. `AccessDenied`: role/policy/bucket policy; `NoSuchKey` is success; throttling is retryable.
2. Fix the cause, then force a retry: `UPDATE files SET next_deletion_at = now() WHERE id = $1;`
3. If urgent legal deletion: delete the object with the instance role, then record a manual audit
   note with two-person approval.
4. Verify the object is gone and the row is `deleted` with an audit event.

## Outbox lag

```sql
SELECT state, count(*), min(available_at) FROM outbox_messages
WHERE state IN ('pending','processing') GROUP BY state;
SELECT * FROM outbox_messages WHERE state='dead' ORDER BY created_at DESC LIMIT 20;
```

1. Check the worker container and logs; restart it if the scheduler is wedged (advisory locks
   release with the connection).
2. Dead letters: inspect `last_error`; fix the handler/input, reset to `pending`, attempts=0.
3. Verify `outbox_lag_seconds` <60 s and `job_last_success_timestamp_seconds` advances.

## Email failures

1. Check `notification_delivery_failures_total{reason}` and Graph responses.
2. 429/5xx: retries continue; reduce batch size if throttled. 403: verify `Mail.Send`, application
   access policy, and mailbox. 400: invalid recipient.
3. Reset permanent `failed` events with attempts<5 to `scheduled` after fixing the cause.
4. Expiration/deletion continue regardless. Verify accepted vs delivered (provider acceptance is
   not inbox delivery).

## Download abuse

1. Dashboard: failures/lockouts by link, hashed IPs, grant rate.
2. Single link: revoke it (`DELETE /v1/portal/links/{id}`) or soft-lock; many links: disable public
   links in `platform_settings` after approval.
3. Check `download_events` for any `completed` during the attack; notify the owner if found.
4. Post-incident: rotate the link (new token/key), review limits, consider WAF/edge blocking.

## Database

1. `docker compose exec postgres pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"`.
2. Connections: `pg_stat_activity`; slow queries; missing indexes; lock waits.
3. Disk full: see §3; do not delete PostgreSQL files. Stop API/worker, free space, restart.
4. Corruption/failed upgrade: stop API/worker, preserve the volume, restore from the latest
   `pg_dump` into a fresh database (see §11), then repoint `.env` and start.
5. Verify app-level smoke tests and counts.

## Backup failure

**Backup job** (cron/systemd timer):

```bash
cd /opt/files/app
set -a; . /opt/files/.env; set +a
./deploy/backup.sh
aws s3 ls "s3://$S3__Bucket/backups/postgres/" | tail
```

1. If the dump failed: check disk, DB health, AWS CLI/instance role, and the cron logs.
2. Run manually; confirm size >0; verify the `_latest` marker object.
3. Restore into a fresh instance/database:
   - Download the chosen dump.
   - Stop API/worker.
   - Create a new database, `pg_restore --clean --if-exists -d files dump`.
   - Point `ConnectionStrings__FilesDb` at it, start containers, run smoke tests.
   - Record the dump timestamp and data-loss window.
4. Never restore over the only copy without preserving it first.

## TLS

1. Check Caddy logs for ACME errors; verify DNS A record → Elastic IP and ports 80/443 reachable.
2. Ensure the domain is correct in `FILES_DOMAIN` and the Caddyfile; no IPv6-only mismatch.
3. If rate-limited, wait or use a different domain/issuer; do not disable TLS.
4. Verify: certificate chain, expiry >14 days, HTTP→HTTPS redirect, HSTS.

## Credential issues

- Entra 401s: audience/tenant/issuer, app registration, secret/cert expiry, clock skew.
- API key 401s: key revoked/expired, wrong `kid`, integration disabled; issue a replacement.
- Leaked key: revoke immediately, issue new, review audit/download events, notify security.
- Verify: `/v1/me` (user) or a metadata call (integration) succeeds.

## Reconciliation

1. `reconcile_missing_db_total>0`: files without objects are quarantined; check S3 lifecycle or
   accidental deletes.
2. Orphans under `f/` without DB rows: verify they are not active sessions; the job moves them to
   `orphan/` and lifecycle deletes after 7 days.
3. Do not restore availability automatically; investigate and inform the owner.

## Cost anomaly

1. AWS Budgets/anomaly detection; identify EC2, EBS, S3 storage/egress.
2. Egress spike: top links/IPs (hashed), revoke abusive links, cap public links.
3. EBS growth: staging cleanup, backups, PostgreSQL bloat; resize only after cleanup.
4. Verify budgets and quotas are updated; record the cause.

## Pepper rotation

1. Generate a new 32-byte pepper; keep both versions in `.env`
   (`Auth__ApiKeyPepperVersion`/`Auth__LinkKeyPepperVersion` and versioned values).
2. New credentials/links use the new version; old hashes verify with their stored
   `hash_key_version`/`download_key_version`.
3. API keys: re-issue, migrate, revoke old ones.
4. Link keys cannot be rehashed; old links keep the old pepper until revoked/expired. Do not
   remove an old pepper while old links exist.
5. Restart the API and verify authentication.

## Emergency controls

| Control | Action | Reversible |
|---|---|---|
| Disable public links | `platform_settings.public_links_enabled=false` | Yes |
| Disable an integration | PATCH status=disabled with reason | Yes |
| Revoke credentials | Admin API revoke | No (re-issue) |
| Stop uploads | Scale API to 0 or block chunk routes at Caddy | Yes (service impact) |
| Pause deletion | Stop worker (queue grows) | Yes |
| Pause notifications | `notifications_enabled=false` | Yes |
| Maintenance page | Caddy `respond 503` for API paths | Yes |

Record every emergency action in the incident.

## Drill log

| Date | Drill | Result | RTO/RPO | Actions |
|---|---|---|---|---|
| _TBD_ | Restore production `pg_dump` into a fresh instance | Not run | — | Schedule before launch |
| _TBD_ | Disk-fill / staging reserve test | Not run | — | Tabletop |
| _TBD_ | TLS renewal after DNS change | Not run | — | Tabletop |
