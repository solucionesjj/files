# ADR-0006 — Retention model: explicit finite/indefinite modes

- **Status:** Accepted (defaults provisional per OQ-07)
- **Date:** 2026-10-23
- **Deciders:** Product, Architecture, Legal

## Context

The source requires distinguishing finite retention with an expiry from indefinite retention
without one, and explicitly forbids representing indefinite retention as a far-future timestamp.
Global defaults change over time but must not silently mutate existing files. Integration-owned
files default to indefinite.

## Decision

- `files.retention_mode` is `finite` or `indefinite`.
- `files.expires_at` is `timestamptz NULL`; a check constraint enforces
  `(mode = 'finite') = (expires_at IS NOT NULL)`.
- `files.retention_policy_id` snapshots the policy version used when the expiry was set/updated;
  `files.retention_days` stores the applied number of days for finite files (nullable).
- Retention policy is versioned in `retention_policies` (immutable rows, one `is_active`).
  Fields: `default_days`, `max_days`, `allow_user_override`, `allow_indefinite`,
  `notification_intervals_days int[]`, `public_links_enabled`, `effective_from`, `created_by`.
- New human files get the active policy defaults at creation. Existing files keep their expiry.
  Changing the default affects only new files.
- Admins can run an explicit bulk migration (`POST /v1/admin/retention/migrations`) with a
  scope + reason; each affected file is audited and its notifications recalculated.
- Integration files are created `finite? no` → `indefinite`, `expires_at NULL`. Only an admin
  override (reason required) or an explicit authorized delete changes that.
- Notification intervals are stored on the active policy and snapshotted onto scheduled
  notification events (`interval_days`, `expiration_at`) to avoid duplicates.

## Consequences

- Expiration queries use `expires_at <= now()` only; no magic dates and no "9999-12-31" rows.
- Policy history is preserved; `retention_policies` is append-only (deactivate + insert).
- Bulk migrations can be large; they run in batches in the worker with progress tracked in
  `retention_migrations`/audit, not synchronously in the request.
- UI must render indefinite as "Indefinite", never a date.

## Alternatives considered

- `expires_at = NULL` only, no mode: rejected (ambiguous when a finite file loses its date).
- Far-future sentinel: explicitly rejected by the spec.
- Mutating all files when the default changes: rejected (surprising data loss/extension).
