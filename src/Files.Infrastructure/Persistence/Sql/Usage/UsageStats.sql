-- Aggregate usage (FR-ADM-006). Four result sets, consumed with QueryMultiple:
--   1) storage totals, 2) file counts, 3) completed download bytes, 4) per-integration usage.
-- Storage is measured as files that still hold an object key (tombstones purged by V6 have
-- storage_key NULL and are excluded).
SELECT
    COALESCE(SUM(size_bytes), 0)::bigint AS "StorageTotalBytes",
    COALESCE(SUM(size_bytes) FILTER (WHERE owner_type = 'human'), 0)::bigint AS "StorageHumanBytes",
    COALESCE(SUM(size_bytes) FILTER (WHERE owner_type = 'integration'), 0)::bigint AS "StorageIntegrationBytes"
FROM files
WHERE storage_key IS NOT NULL;

SELECT
    COUNT(*) AS "FilesTotal",
    COUNT(*) FILTER (WHERE status = 'available') AS "FilesAvailable",
    COUNT(*) FILTER (WHERE status = 'expired') AS "FilesExpired",
    COUNT(*) FILTER (WHERE status = 'pending_deletion') AS "FilesPendingDeletion"
FROM files;

SELECT
    COALESCE(SUM(bytes_sent) FILTER (WHERE occurred_at >= now() - interval '30 days'), 0)::bigint AS "DownloadsLast30Days",
    COALESCE(SUM(bytes_sent) FILTER (WHERE occurred_at >= now() - interval '7 days'), 0)::bigint AS "DownloadsLast7Days"
FROM download_events
WHERE result = 'completed';

SELECT
    i.id AS "IntegrationId",
    i.name AS "Name",
    COUNT(f.id) AS "Files",
    COALESCE(SUM(f.size_bytes), 0)::bigint AS "Bytes"
FROM integrations i
LEFT JOIN files f ON f.owner_integration_id = i.id AND f.storage_key IS NOT NULL
GROUP BY i.id, i.name
ORDER BY "Bytes" DESC, i.name;
