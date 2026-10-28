-- Mark an issued/redeemed grant failed (unavailable file, missing object, or aborted stream) and
-- record the attempt. No counter is incremented (counts are completion-only, ADR-0005).
WITH failed AS (
    UPDATE download_grants
       SET state = 'failed', updated_at = now()
     WHERE id = @GrantId AND state IN ('issued', 'redeemed')
     RETURNING id, file_id, link_id, source, issued_to_user_id
)
INSERT INTO download_events (file_id, link_id, grant_id, actor_type, actor_id, source, result, reason_code, bytes_sent)
SELECT file_id, link_id, id, 'user', issued_to_user_id, source, 'failed', @ReasonCode, 0
  FROM failed;
