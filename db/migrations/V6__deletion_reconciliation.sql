-- Files — physical deletion, tombstones, and reconciliation (S11/M6)
-- Forward-only; see AGENTS.md §4 (immutable migrations, expand/contract).
-- No table or column is dropped. The tombstone purge nulls `storage_key` on `deleted` rows past
-- the 60-month audit window (ADR-0008), so the column becomes nullable; the deletion pipeline
-- itself reuses the status/deleted_at/deletion_attempts/next_deletion_at/last_deletion_error and
-- quarantine_reason columns introduced in V1.

-- Tombstone purge: `deleted` rows retain all metadata except the storage key, which is nulled.
ALTER TABLE files ALTER COLUMN storage_key DROP NOT NULL;

-- Tombstone purge candidate scan: `deleted` rows that still retain a storage key, oldest first.
CREATE INDEX ix_files_deleted_purge
    ON files (deleted_at)
    WHERE status = 'deleted'
      AND storage_key IS NOT NULL;

-- Reconciliation DB→S3: keyset page over `available` rows by id so the job can walk
-- every live object's metadata without an unbounded sort.
CREATE INDEX ix_files_available_reconcile
    ON files (id)
    WHERE status = 'available';

