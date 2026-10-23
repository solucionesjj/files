# ADR-0007 — Background processing: transactional outbox, advisory locks, idempotency

- **Status:** Accepted
- **Date:** 2026-10-23
- **Deciders:** Architecture

## Context

Notifications, expiration, deletion, cleanup, and reconciliation must be idempotent, retryable,
and safe when multiple API/worker replicas run. Side effects (email, audit projections) must not
be lost when a transaction commits but the process crashes before the effect, and must not be
duplicated when a job runs twice.

## Decision

**Outbox**

- Domain events are inserted into `outbox_messages` in the same transaction as the state change.
- The worker's dispatcher claims rows with `FOR UPDATE SKIP LOCKED` in batches (100), dispatches
  to in-process handlers, marks `processed_at`, and records `attempts`/`last_error`. Rows stuck in
  `processing` for more than 5 minutes are reclaimed (crash recovery).
- At-least-once delivery; handlers must be idempotent (unique keys, upserts, or state checks).
- After 10 failed attempts a row moves to `dead` and raises an alert; it can be replayed by an
  admin operation or a CLI.

**Scheduling**

- Quartz.NET with an in-memory job store (no Quartz tables). Job definitions are code-config.
- Every execution is wrapped in `pg_try_advisory_lock(hash(job_name))`; a second replica skips
  the run. Locks are session-scoped and released automatically on connection loss.
- Jobs are batch-limited and resumable; state lives in the database, never in memory.
- Schedules are UTC cron expressions documented in the workflow docs; misfires are ignored with
  a threshold (run once on recovery).

**Idempotency for API clients**

- `idempotency_records` stores (`scope_type`, `scope_id`, `key`) unique, request hash, state
  (`in_progress`/`completed`), response status/body, and expiry (24 h).
- The record is written in the same transaction as the operation. Concurrent duplicates return
  409 `idempotency_in_progress`; a reused key with a different body returns 422
  `idempotency_key_reuse`; completed duplicates replay the stored response.

**Correctness backstops**

- Unique constraints: `download_links.token_hash`, `download_grants.token_hash`,
  `notification_events(file_id, interval_days, expiration_at)`,
  `files.storage_key`, `integration_credentials.key_id`.
- State checks (`status = 'available'`, `expires_at > now()`) before every side effect.
- All timestamp comparisons use database `now()` in transactional SQL.

## Consequences

- No external message broker in v1; Postgres carries the queue. Table vacuum/partitioning may be
  needed at high volume (outbox rows are short-lived and purged).
- Advisory locks are per database; if the DB is unavailable no jobs run (acceptable, fail closed).
- Outbox lag is an SLO metric (`outbox_lag_seconds`) and alerts above 60 s.
- Job handlers are plain application services, testable without the scheduler.

## Alternatives considered

- Quartz `AdoJobStore`: rejected (11 extra tables, lock contention, heavier than needed).
- Hangfire: rejected (dashboard/DB schema, licensing considerations, retry semantics tied to
  its storage).
- SQS/Service Bus: rejected for v1 (extra component); the outbox abstraction allows a future
  transport swap.
- `LISTEN/NOTIFY` instead of polling: rejected (connection complexity, missed events); polling
  every 2 s is sufficient.
