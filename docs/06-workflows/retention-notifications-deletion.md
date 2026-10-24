# Retention, Notifications, and Deletion Workflows

**Decisions:** ADR-0006, ADR-0008, ADR-0009, ADR-0007 · **Lifecycle:** [file-lifecycle.md](file-lifecycle.md)

## 1. Retention configuration

The active `retention_policies` row drives new files:

| Field | Meaning | Default |
|---|---|---|
| `default_days` | Applied to new human files. | 30 |
| `max_days` | Absolute upper bound for finite retention. | 365 |
| `allow_user_override` | Can owners change retention? | true |
| `allow_indefinite` | Can human files have indefinite retention? | false |
| `public_links_enabled` | Global public-link switch. | true |
| `effective_from` | When this version became active. | now() |

A user may extend retention above the current default up to 365 days only with a mandatory note
stored on the file and audit event. There are no administrative exceptions above 365 days.

Policies are immutable versions: publishing a new one deactivates the previous row (partial
unique index enforces one active). Files snapshot `retention_policy_id` and `retention_days` when
their expiry is set or changed. Changing the active policy affects **new files only**; existing
files keep their expiry until an explicit admin migration.

Integration files are always created with `retention_mode='indefinite'`, `expires_at=NULL`, and
are never included in notification scheduling. Only an admin override (audited) or deletion
changes their retention/lifecycle.

## 2. Notification scheduling

Active `notification_policies.intervals_days` (default `{7,3,1}`) are applied to human files.

### Scheduling algorithm (hourly)

For each interval `i` in the active policy and each file that is `available`, human-owned, finite,
not legal-hold, with an email (held files have notifications suspended):

```
due_at = expires_at - (i * interval '1 day')

if due_at <= now():
    -- The file was created/updated inside the window; do not send retroactively.
    skip
else:
    INSERT INTO notification_events (file_id, policy_id, interval_days, expiration_at,
                                     recipient_email, status, next_attempt_at)
    VALUES (..., 'scheduled', due_at)
    ON CONFLICT (file_id, interval_days, expiration_at) DO UPDATE
       SET status = 'scheduled', attempts = 0, last_error = NULL,
           next_attempt_at = excluded.next_attempt_at
     WHERE notification_events.status = 'superseded'
```

The `DO UPDATE ... WHERE status = 'superseded'` revives an event when a file's expiry moved away
and back to the same timestamp. Rows that were already sent/accepted are never revived (that would
duplicate an email).

- `expiration_at` snapshots the file's expiry; the unique constraint
  (`file_id, interval_days, expiration_at`) makes scheduling idempotent.
- Multiple intervals can become due in the same run (e.g., job outage); each is sent at most once.
- Files with `expires_at` in the past are expired by the expiration job, not notified.
- If the owner has no email, the event is created as `failed` with `last_error='no_recipient'`
  and no retry; an alert fires if this happens for new uploads.
- Notifications are suppressed for indefinite retention, integration-owned files, deleted files,
  and files whose expiry is being extended past the interval.

### Recalculation triggers

| Event | Action |
|---|---|
| File expiry changes (owner/admin) | Mark scheduled notifications with the old `expiration_at` as `superseded`; schedule again for the new expiry (skip intervals already past). |
| File deleted/expired/quarantined | Mark all `scheduled`/`failed` notifications `superseded`. |
| Notification policy changes | Applies to **future scheduling**; already scheduled events keep their interval/expiry snapshot. On the next run, new intervals are scheduled if still in the future. |
| Email changes (login) | Future events use the latest email; existing scheduled events are updated to the new address only if still `scheduled`. |
| File becomes unavailable | Suppress pending notifications. |

`superseded` events are never sent. The unique constraint allows a new row for the new expiry.

## 3. Email rendering and delivery

- Templates: `email_templates` keyed by (`key`, `locale`), active version. The policy's
  `template_key` selects the family; the file owner's locale is the UI locale (`es-CO` by default),
  falling back to `en-US`, then the newest active version.
- Variables: `fileName` (HTML-escaped), `expiresAt` (localized date), `intervalDays`, `ownerName`,
  `portalUrl`, `fileId`. Strict allowlist; unknown placeholders cause a rendering failure (no
  send) rather than leaking data.
- Never include: file content, object keys, download links/keys, credentials, other users' data.
- Sending via Microsoft 365 SMTP AUTH (ADR-0009): batch 50/minute, SMTP 2xx → `accepted`; 4xx/5xx →
  retry; 400/403 → `failed` (permanent).
- Retry policy: attempts 1..5 with backoff 1 m, 5 m, 15 m, 1 h, 6 h (+jitter). After 5 failures →
  `failed`, metric + alert; operator can reset via a runbook/CLI.
- Delivery status: provider acceptance is **not** confirmed delivery. If NDR/message-trace
  ingestion is enabled, update to `delivered`/`bounced`; otherwise the UI shows "accepted".
- Idempotency: one send per `notification_events` row; the sender uses
  `UPDATE ... SET status='sending' WHERE id=$1 AND status IN ('scheduled','failed')` to claim.
  Rows stuck in `sending` for more than 10 minutes are reclaimed as `failed` with
  `last_error='sender_timeout'` before retry. A crash after sending but before recording may
  duplicate one email; this is accepted (email is not the source of truth). Attempts are recorded
  in `notification_delivery_attempts`.

## 4. Expiration job

Runs every 15 minutes (UTC), advisory lock `files:expire`, batch 500.

```sql
-- candidates
SELECT id FROM files
 WHERE status = 'available'
   AND expires_at IS NOT NULL
   AND expires_at <= now()
   AND legal_hold = false
 ORDER BY expires_at
 LIMIT 500
 FOR UPDATE SKIP LOCKED;
```

For each file (one transaction per batch):

1. `UPDATE files SET status='pending_deletion', expires_at` unchanged, `next_deletion_at=now()`
   (only rows with `legal_hold=false`),
   `deletion_requested_by_type='system'`, `deletion_reason='retention_expired'`,
   `version=version+1` where `status='available'` and `expires_at <= now()`.
2. Revoke active links (`status='revoked'`, `revoked_at=now()`, reason `file_expired`).
3. Supersede pending notifications.
4. Write `file_audit_events` (`expired`), outbox `file.expired`, metric
   `files_expired_total`.

Downloads are already blocked by the eligibility predicate (expiry check) even before the job
runs; the job only makes the state explicit and schedules deletion. A file is never downloadable
while `expired`/`pending_deletion`.

## 5. Deletion job

Runs every 5 minutes, lock `files:delete`, batch 100.

For each `pending_deletion` file with `next_deletion_at <= now()` and `legal_hold=false`:

1. Call S3 `DeleteObject` (idempotent; `NoSuchKey`/404 = success).
2. On success: `status='deleted'`, `deleted_at=now()`, `last_deletion_error=NULL`,
   `next_deletion_at=NULL`, audit `deleted`, outbox `file.deleted`.
3. On retryable failure: `deletion_attempts+1`, `next_deletion_at = now() + backoff`,
   `last_deletion_error` (sanitized), stay `pending_deletion`, metric
   `files_deletion_failures_total{reason}`, alert if pending > 15 min.
4. On permanent failure (403/404 bucket): alert immediately; file stays non-downloadable.

Backoff: `min(1h, 1m * 2^(attempts-1))` with ±20% jitter. After 10 attempts, alert severity high
and keep retrying hourly until manual intervention.

Tombstones (`status='deleted'`) retain metadata for 60 months (audit) and are then purged by the
retention purge job; the storage key is nulled only at purge.

## 6. Upload cleanup and reconciliation

### Upload cleanup (hourly)

- Sessions in `initiated|uploading|received` with `expires_at <= now()` (not `committing` or
  `processing`) → delete the local staging file, abort any S3 multipart, mark `upload_failed`,
  file `upload_failed` (then
  `pending_deletion` on a later run), audit.
- S3 multipart uploads older than 24 h → `AbortMultipartUpload` (best effort; bucket lifecycle
  aborts at 7 days as a backstop).
- Local staging files with no open session, or older than 24 h, → delete.
- `staging/` S3 objects older than 1 day → delete.

### Reconciliation (daily, 02:00 UTC)

1. **DB → S3:** for a sample or all `available` files, `HeadObject`; missing → quarantine
   (`quarantine_reason='object_missing'`), revoke links, alert, audit.
2. **S3 → DB:** use an S3 Inventory report (preferred) or a bounded `ListObjectsV2` scan:
   - objects under `f/` with no file row → move to `orphan/` (copy+delete); the 7-day
     lifecycle rule removes them; alert with a key fingerprint (never the full key).
   - `staging/` objects with no open session → delete; orphan local staging files → delete.
3. Report counts: `reconcile_missing_db_total`, `reconcile_orphan_s3_total`,
   `reconcile_quarantined_total`; store the run in logs (and optionally a table if reporting is
   needed later).

Reconciliation never "fixes" a mismatch by restoring availability; it fails closed.

## 7. Admin bulk retention migration

`POST /v1/admin/retention-migrations` creates a `retention_migrations` row (`pending`) with a
scope (owner type/id, status, created range) and a target policy. The worker:

1. Claims the row (`UPDATE ... SET status='running' WHERE id=$1 AND status='pending'`).
2. Processes files in batches (500) with `FOR UPDATE SKIP LOCKED`, applying `target_policy`:
   - finite target: `expires_at = created_at + target.default_days` if that is in the future,
     otherwise `now()`; `retention_mode='finite'`, `retention_days=target.default_days`,
     `retention_policy_id=target.id`;
   - integration files are skipped unless the scope explicitly includes them and the admin
     confirmed (v1: skipped).
3. Supersedes and recalculates notifications per file; audits each change with
   `reason='admin_retention_migration'` and the migration ID.
4. Updates counters and finishes `completed`/`failed`; a failure leaves partial progress recorded
   and can be retried by creating a new migration.

No synchronous mass update in the request path.

## 8. Job schedule

| Job | Cron (UTC) | Lock |
|---|---|---|
| Outbox dispatcher | continuous (2 s poll) | `files:outbox` per row |
| Notification scheduler | `5 * * * *` | `files:notify-schedule` |
| Notification sender | `* * * * *` | `files:notify-send` |
| Expiration | `*/15 * * * *` | `files:expire` |
| Deletion | `*/5 * * * *` | `files:delete` |
| Grant expiry | `* * * * *` | `files:grant-expiry` |
| Link expiry | `*/15 * * * *` | `files:link-expiry` |
| Upload cleanup | `20 * * * *` | `files:upload-cleanup` |
| Reconciliation | `0 2 * * *` | `files:reconcile` |
| Data retention purge | `0 3 * * *` | `files:purge` |

Each job is wrapped in `pg_try_advisory_lock(hashtext(name))`, processes a bounded batch, and
records duration/result metrics. Misfires run once on recovery (state is in the database).

## 9. Metrics and alerts

| Metric | Alert |
|---|---|
| `files_expired_total` | Informational. |
| `files_deletion_failures_total` | >5 pending for 15 min → page. |
| `notification_delivery_failures_total` | >10 in 1 h → ticket. |
| `notification_events_pending` | >1000 or oldest > 2 h → page. |
| `outbox_lag_seconds` | >60 s → page. |
| `job_last_success_timestamp_seconds` | age > 2× schedule → page. |
| `reconcile_missing_db_total` | >0 → ticket; >10 → page. |
| `reconcile_orphan_s3_total` | >100 → ticket. |
| `download_completion_lost_total` | >0.1% of downloads → investigate. |

## 10. Edge cases

- **Upload created inside the notification window:** no retroactive emails; only future intervals
  are scheduled. The UI warns "expires in X days" immediately.
- **Retention extended after a notification was sent:** old scheduled events are superseded; the
  sent email stays in history; new intervals are scheduled relative to the new expiry.
- **Retention shortened below now:** the file is expired on the next run; pending notifications are
  superseded; links revoked.
- **Email missing/changed:** no send / updated address for scheduled events.
- **Clock skew:** all comparisons use database `now()`; app clocks only drive the scheduler.
- **DST:** schedules are UTC; user-facing dates are localized.
- **Integration files:** no notifications, indefinite retention, deletion only by owner/admin.
- **Legal hold:** `legal_hold=true` blocks expiry and deletion. Scheduled expiration emails are
  superseded on hold and rebuilt on release; every hold/release is audited.
- **Backups:** deletion removes the object from the primary bucket; nightly logical database
  backups in `backups/postgres/` may retain metadata copies for up to 35 days (ADR-0008). S3
  objects themselves are not replicated, so file erasure is immediate (subject to EBS snapshots,
  if used).
