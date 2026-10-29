-- Files — retention, legal hold, and notifications (S10/M6)
-- Forward-only; see AGENTS.md §4 (immutable migrations, expand/contract).
-- No schema is dropped. This migration aligns the notification delivery attempt
-- provider default with ADR-0009 (SMTP, never Graph) and adds indexes the
-- notification scheduler/sender and expiration job rely on.

-- SMTP is the only outbound path (ADR-0009); POP is receive-only and Graph is out of scope.
ALTER TABLE notification_delivery_attempts
    ALTER COLUMN provider SET DEFAULT 'smtp';

-- Notification scheduler candidate scan: finite, human-owned, available, non-held files.
CREATE INDEX ix_files_notification_candidates
    ON files (expires_at)
    WHERE status = 'available'
      AND owner_type = 'human'
      AND retention_mode = 'finite'
      AND expires_at IS NOT NULL
      AND legal_hold = false;

-- Sender reclaim of events stuck in 'sending' beyond the 10-minute lease.
CREATE INDEX ix_notifications_sending
    ON notification_events (updated_at)
    WHERE status = 'sending';
