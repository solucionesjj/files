-- Append a denied/failed integration download attempt (no counter increment). `@FileId` may be
-- NULL for a foreign/missing file so the FK never fails and no existence oracle is created.
INSERT INTO download_events (file_id, actor_type, actor_id, source, result, reason_code, bytes_sent)
VALUES (@FileId, 'integration', @IntegrationId, 'integration', @Result, @ReasonCode, 0);
