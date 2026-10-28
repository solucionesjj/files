-- V4 — scan/commit worker retry state (S5, ADR-0003 / D-35).
--
-- The scan/commit worker claims files in `processing` with `scan_status IN ('pending',
-- 'failed')`. `scan_attempts` drives exponential backoff after a scanner failure, and
-- `scan_next_attempt_at` doubles as (a) the claim lease — set to `now()` while a worker scans so
-- a concurrent worker never double-processes the same file — and (b) the next eligible retry time
-- on failure. A crashed worker leaves the lease to expire, after which the file is reclaimed.
-- The terminal 24 h window is still enforced by UploadCleanupService via `upload_sessions.updated_at`.

ALTER TABLE files
    ADD COLUMN scan_attempts integer NOT NULL DEFAULT 0 CHECK (scan_attempts >= 0),
    ADD COLUMN scan_next_attempt_at timestamptz;

-- The pending path is already served by ix_files_scan_pending (created_at WHERE scan_status =
-- 'pending'); this index serves the failed-retry lookup ordered by the next eligible time.
CREATE INDEX ix_files_scan_retry ON files (scan_next_attempt_at)
    WHERE status = 'processing' AND scan_status = 'failed';
