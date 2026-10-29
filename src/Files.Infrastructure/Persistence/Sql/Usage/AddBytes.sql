-- Upsert the integration's daily transfer accounting with an atomic increment (FR-INT-010).
INSERT INTO daily_usage (scope_type, scope_id, usage_date, bytes_uploaded, bytes_downloaded)
VALUES ('integration', @ScopeId, @Date, @UploadedBytes, @DownloadedBytes)
ON CONFLICT (scope_type, scope_id, usage_date)
DO UPDATE SET
    bytes_uploaded   = daily_usage.bytes_uploaded + EXCLUDED.bytes_uploaded,
    bytes_downloaded = daily_usage.bytes_downloaded + EXCLUDED.bytes_downloaded,
    updated_at       = now();
