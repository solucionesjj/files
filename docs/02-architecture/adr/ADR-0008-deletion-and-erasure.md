# ADR-0008 — Deletion and erasure semantics

- **Status:** Accepted
- **Date:** 2026-10-23
- **Deciders:** Architecture, Security, Legal

## Context

The source requires that expired files stop being downloadable before physical deletion, that
logical expiration be distinguished from successful physical deletion and recoverable failures,
that deletion be idempotent, and that the product not claim complete erasure while backups or
replicas retain copies.

## Decision

**States**

`available → expired → pending_deletion → deleted` (see the lifecycle model). Manual deletion
sets `pending_deletion` immediately and revokes all links. `deleted` is terminal; the metadata
row remains as a tombstone with `storage_key` retained for audit but never exposed.

**Physical deletion**

- The worker calls S3 `DeleteObject`; `NoSuchKey`/404 is treated as success (idempotent).
- On success: `deleted_at = now()`, `storage_key` retained, `deletion_error = null`.
- On retryable failure (5xx, timeout, throttling): increment `deletion_attempts`, set
  `next_deletion_at` with exponential backoff (1 min → 1 h, jitter), keep `pending_deletion`,
  emit `files_deletion_failures_total`, alert after 15 min.
- On non-retryable failure (access denied): same state, alert immediately; operator runbook.
- A reconciliation job detects deleted rows whose object still exists and re-queues deletion, and
  `available` rows whose object is missing (quarantine + alert).

**Backups and replicas**

- A nightly logical `pg_dump` is written to `s3://<bucket>/backups/postgres/` and retained 35 days
  by lifecycle; it is separate from the live table, so deleted rows persist there until the
  backup window passes. There is no PITR/WAL archiving in the initial single-node phase.
- Optional EBS snapshots (manual or scheduled outside Terraform) are treated as another backup
  copy with the same erasure caveat.
- S3 versioning is **disabled** in production to avoid hidden copies; lifecycle abort rules clean
  incomplete uploads. If versioning is enabled for operational reasons, a lifecycle rule must
  expire noncurrent versions within 24 h and this ADR must be revisited.
- Any cross-region replication (if enabled) is documented, and deletion propagates via the
  provider; the product never promises "immediate, complete, global erasure".
- The public product wording: "Files removes the object from primary storage immediately after
  logical deletion; logical database backups are retained for up to 35 days under the backup
  policy."

**Audit**

- `file_audit_events` records the deletion request (actor, reason), each attempt/result, and the
  final state. Tombstones are purged after 24 months (configurable) — after that, only audit
  aggregates remain (if required) or nothing.

## Consequences

- No hard delete of metadata in the deletion path; storage growth is bounded by tombstone purge.
- Deletion is eventually consistent with S3; the API never advertises a file as available after
  `expires_at` regardless of deletion state.
- Legal hold (OQ-08) can be added as a `legal_hold` flag that blocks the transition; the column
  is present but unused in v1.

## Alternatives considered

- Delete DB row immediately and rely on S3 lifecycle: rejected (no per-file audit, no guarantee,
  lifecycle is daily).
- Synchronous S3 delete in the request: rejected (latency, retry semantics, coupling).
- Claim immediate erasure: rejected as misleading (backups).
