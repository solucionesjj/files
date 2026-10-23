# Operational Runbooks

Each runbook: symptoms → impact → immediate actions → diagnosis → remediation → verification.
Commands use placeholders (`$ENV`, `$BUCKET`, `$CLUSTER`) exported by the operator. Never paste
secrets into shells that log history; use the secret manager/SSM session.

## 0. First response (all incidents)

1. Check SigNoz **API RED**, **Uploads/Downloads**, **Jobs**, and CloudWatch alarms.
2. Check the latest deployment: `aws ecs describe-services --cluster $CLUSTER --services files-api files-worker`
   (look at task definition revision, events, running/desired counts).
3. Determine scope: all users, one integration, one link, one file, one region/DB.
4. If a recent deploy correlates, consider rollback before deep diagnosis
   ([deployment-and-rollback.md](deployment-and-rollback.md)).
5. Open an incident channel, assign commander, record timeline. Preserve logs/traces.

---

## 1. API errors (`#api-errors`)

**Symptoms:** 5xx rate >1%, readiness failing, Sentry/SigNoz exception spike.

1. Split by route/status: `http_server_requests_total{status_class="5xx"}` dashboard.
2. Check DB (`pg_isready`, RDS alarms), S3 (`s3_requests_total{result="error"}`), Entra/Graph
   (dependencies), and recent deploys.
3. Look for a single error code in logs (`errorCode`) and the top trace IDs.
4. If DB connections exhausted: check `db_pool_connections`, long-running queries
   (`pg_stat_activity`), kill only obvious runaway queries after approval.
5. If S3 throttling (503 SlowDown): enable SDK retry/backoff (already on), reduce concurrency,
   check request rate spike.
6. Rollback if the error started with a deploy and the cause is unclear.

**Verify:** 5xx <0.1% for 15 min, readiness green, smoke tests pass.

## 2. Latency (`#latency`)

1. Identify route: p95/p99 by route; compare with SLO.
2. Check DB slow queries (`pg_stat_statements`), missing indexes (`EXPLAIN`), lock waits
   (`pg_locks`), and connection pool saturation.
3. Check S3 latency/TTFB and NAT/egress throughput.
4. Check CPU/memory of API tasks; scale out if saturation (autoscaling should handle; verify
   max capacity).
5. For a single slow endpoint, inspect recent code/config changes; add a temporary cache only if
   safe (no authorization leakage).

## 3. Dependency outage (`#dependency-outage`)

**Database:**
- API returns 503; readiness fails; logs show Npgsql timeouts.
- Fail closed: do not bypass DB checks. Check RDS events, CPU, connections, storage, network ACL/SG.
- If failover: wait for Multi-AZ DNS; restart tasks only if connections do not recover.
- If storage full: expand storage (autoscaling), purge only via approved jobs.

**S3:**
- Uploads/downloads fail; metadata still works.
- Check AWS Health Dashboard and S3 service status; check IAM role/credentials (task role),
  bucket policy, KMS permissions, VPC endpoint.
- Do not rotate credentials blindly; confirm the error (`AccessDenied` vs `SlowDown` vs network).
- Communicate degraded uploads/downloads; retries are safe.

**Entra ID:**
- New logins/token acquisition fail; existing tokens work until expiry.
- Check Entra service health; verify app secrets/certificates (Graph) and JWKS reachability.
- Do not disable auth to restore service.

**Graph (email):**
- Notifications queue and retry; expiration/deletion unaffected. See §6.

## 4. S3 access denied (`#s3-outage`)

1. Identify the operation and error code from `s3_requests_total` and logs (no keys logged).
2. Verify the task role ARN and policy attachment:
   `aws sts get-caller-identity` is not available inside Fargate; use CloudTrail events for the
   role and the bucket policy.
3. Check bucket policy/BPA changes, KMS key policy (if SSE-KMS), object ownership, and VPC endpoint
   policy.
4. Check whether the key prefix is allowed (`f/*`, `staging/*`) — a new prefix requires a policy
   update.
5. Fix IAM/bucket policy via Terraform (preferred) and roll the service; never grant `s3:*`.

## 5. Deletion failures (`#deletion-failures`)

**Symptoms:** `files_pending_deletion` grows, `file_deletion_failures_total>0`, alert after 15 min.

```sql
SELECT id, owner_type, deletion_attempts, next_deletion_at, last_deletion_error
FROM files
WHERE status = 'pending_deletion'
ORDER BY next_deletion_at
LIMIT 50;
```

1. Inspect `last_deletion_error` (sanitized). Common: `AccessDenied`, `NoSuchBucket`, throttling,
   network.
2. `AccessDenied`: check task role/policy; bucket policy denies deletes? KMS? Object lock/legal
   hold (not in v1)?
3. `NoSuchKey` is success — the job should mark deleted; if not, it is a bug, replay the job.
4. Fix the cause, then force a retry: `UPDATE files SET next_deletion_at = now() WHERE id = $1;`
   (approved change; the job picks it up). Do not set status to deleted manually unless S3 confirms
   deletion and the audit is written.
5. Verify S3 object is gone (`aws s3api head-object`), file is `deleted`, audit event exists.
6. If a file must be deleted urgently for legal reasons, delete the object manually, then mark the
   row with a manual audit note (two-person approval).

## 6. Email failures (`#email-failures`)

1. Check `notification_delivery_failures_total{reason}` and Graph responses (429/5xx/permanent).
2. 429: honor `Retry-After`, reduce batch size; 5xx: transient, retries continue.
3. 403/permission: verify the Entra app permission `Mail.Send`, application access policy, and
   mailbox existence. Fix and replay failed events (`status='failed'` with `attempts<5` can be
   reset to `scheduled`).
4. No-recipient: owner email missing in Entra; contact the user/admin, no retry.
5. Confirm the distinction accepted vs delivered: `accepted` does not mean inbox delivery.
6. Expiration/deletion continue regardless; do not pause jobs to fix email.

## 7. Outbox lag / stuck jobs (`#outbox-lag`, `#stuck-jobs`)

```sql
SELECT state, count(*), min(available_at) FROM outbox_messages
WHERE state IN ('pending','processing') GROUP BY state;
SELECT * FROM outbox_messages WHERE state='dead' ORDER BY created_at DESC LIMIT 20;
```

1. Check worker task health/logs; is the scheduler running? Advisory lock held by a dead session?
   Locks release when the connection closes; restarting the worker clears a stuck in-process lock.
2. Dead letters: inspect `last_error`; fix the handler/input, reset to `pending` with attempts=0.
3. For a poison message, quarantine it (`state='dead'`) and process the rest.
4. Verify `outbox_lag_seconds` <60 s and the job last-success metric advances.
5. Never delete outbox rows to "fix" lag; they carry side effects.

## 8. Download abuse / key lockouts (`#download-abuse`)

1. Dashboard: failures/lockouts by link, source IPs (hashed), and grant rate.
2. Identify whether it is one link (targeted) or many (credential scanning).
3. Immediate: revoke the targeted link (`DELETE /v1/portal/links/{id}`) or soft-lock it; if global,
   disable public links (`platform_settings.public_links_enabled=false`) after approval.
4. Do not reveal whether a guess succeeded; check `download_events` for `result=completed` on the
   link during the attack window and notify the owner if any.
5. Post-incident: shorten link expiry, rotate key (new link), review rate limits, add IP blocking
   at WAF/ALB if available.

## 9. Credential issues (`#credential-issues`)

**Spike of 401s for one integration:**
1. Determine auth type (Entra vs API key) from logs/metrics (no secrets).
2. Entra: token expired/wrong audience/tenant; verify app registration and `azp` mapped to an
   active integration; check client secret/certificate expiry in Entra.
3. API key: check `key_id` prefix, revocation/expiry, and `last_used_at`; issue a replacement key
   and coordinate rotation.
4. Disabled integration: confirm with the owner before re-enabling; audit the reason.

**Revoke a leaked credential immediately:**
`DELETE /v1/admin/credentials/{id}?reason=...`; then issue a new one, review access logs/events,
and notify security.

## 10. Database (`#database`)

- **Storage low:** RDS storage autoscaling; check WAL/table bloat, outbox/event growth, purge jobs.
- **Connections exhausted:** cap pool per task, find leak (unclosed contexts), restart tasks as a
  last resort.
- **CPU high:** slow queries, missing index, job burst; check `pg_stat_activity`.
- **Failover:** wait for DNS/TTL; tasks reconnect (retry policy). Do not force a second failover.
- **Restore (DR):** see §12.

## 11. Certificates (`#certificates`)

1. ACM renewal is automatic if DNS validation is intact; alarm <30 days.
2. Check the certificate status and validation records; re-validate DNS/CNAME.
3. If a new cert is issued, update the ALB listener via Terraform/console and verify TLS chain.
4. SPA host certificate (CloudFront/S3) is separate; check both.

## 12. Disaster recovery restore (`#dr`)

**Declare** after confirming a regional/account-level failure that cannot be fixed within RTO.

1. Provision the DR stack (Terraform with DR region vars) or use the pre-built warm environment.
2. Restore RDS from the latest snapshot/PITR into the DR VPC; record the restore timestamp.
3. Deploy the last known-good image tags; point config at the restored DB.
4. S3 objects are regional; if the primary region is unavailable, restore from the replication
   bucket (if enabled) — otherwise data loss equals the replication gap (documented).
5. Update DNS (Route 53 failover or manual) to the DR ALB; lower TTL beforehand if planned.
6. Smoke tests: health, auth, upload/download, link, admin.
7. Reconcile S3 vs DB; run expiration/deletion jobs cautiously.
8. Communicate status and the RPO/RTO achieved; schedule a postmortem and a drill.

**Quarterly drill:** restore into an isolated account, measure RTO/RPO, record results in this file
(date, backup used, restore duration, issues).

## 13. Cost anomaly (`#cost-anomaly`)

1. AWS Budgets/Anomaly Detection alert → identify service (S3 egress, NAT, Fargate, RDS).
2. S3 egress: top downloaders (hashed IPs), top files/links; check for a viral/leaked public link.
3. NAT: unexpected egress to internet (misconfiguration) or S3 traffic not using the VPC endpoint.
4. Fargate: runaway autoscaling or a stuck job loop.
5. Mitigate: revoke abusive links, cap concurrency/quotas, fix routing, scale down, then review.

## 14. Reconciliation drift (`#reconciliation`)

1. Alert `reconcile_missing_db_total>0`: files marked available without an object.
2. The job quarantines them automatically; verify `status='quarantined'` and the audit event.
3. Check S3 lifecycle expiry, accidental bucket policy changes, or an incomplete migration.
4. Restore from backup/replica only if the object is known to exist elsewhere; otherwise inform
   the owner and delete per policy.
5. Orphans under `f/` without DB rows: verify they are not in-flight uploads (session check),
   then let the grace period move/delete them.

## 15. Pepper rotation (`#pepper-rotation`)

Used for API keys (`Auth__ApiKeyPepper`) and link keys (`Auth__LinkKeyPepper`).

1. Generate a new 32-byte value; add it to the secret store as the **next version** and configure
   the app with both (e.g., `Auth__ApiKeyPeppers__1`, `...__2`).
2. New credentials/links write the new `hash_key_version`/`download_key_version`; existing ones
   verify with their stored version.
3. API keys: re-issue keys for consumers at a controlled pace, then revoke the old ones.
4. Link keys cannot be rehashed without the plaintext; old links keep verifying with the old
   pepper until revoked/expired. Do not remove the old pepper while active links exist.
5. Remove the old pepper only after all old credentials/links are gone; document in the change log.

## 16. Emergency controls

| Control | Action | Reversible |
|---|---|---|
| Disable public links | `platform_settings.public_links_enabled=false` | Yes |
| Disable an integration | PATCH status=disabled with reason | Yes |
| Revoke all credentials of an integration | List + revoke each | No (re-issue) |
| Stop download traffic | Scale API to 0 / ALB rule block | Yes (service impact) |
| Pause deletion | Scale worker to 0 (queue grows) | Yes, but delete backlog grows |
| Pause notifications | `notifications_enabled=false` | Yes |

All emergency actions require an incident record and audit entry.

## 17. Drill log

| Date | Drill | Result | RTO/RPO | Actions |
|---|---|---|---|---|
| _TBD_ | Restore production snapshot to isolated account | Not run | — | Schedule before launch |
| _TBD_ | Disable public links + revoke link | Not run | — | Tabletop |
