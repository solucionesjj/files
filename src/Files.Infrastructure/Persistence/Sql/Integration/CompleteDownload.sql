-- Record a completed integration download in one transaction: increment the file's download
-- counter exactly once and append the `completed` download event (source/actor = integration).
-- No grant or link is involved; counts are completion-only (ADR-0005 / FR-INT).
UPDATE files
   SET successful_downloads = successful_downloads + 1,
       last_download_at = now()
 WHERE id = @FileId;

INSERT INTO download_events (file_id, actor_type, actor_id, source, result, bytes_sent)
VALUES (@FileId, 'integration', @IntegrationId, 'integration', 'completed', @BytesSent);
