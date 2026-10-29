-- Mark an issued/redeemed grant failed (unavailable file, missing object, aborted stream, or a
-- revoked link) and release its reserved slot. No counter is incremented (counts are
-- completion-only, ADR-0005).
WITH failed AS (
    UPDATE download_grants
       SET state = 'failed', updated_at = now()
     WHERE id = @GrantId AND state IN ('issued', 'redeemed')
     RETURNING id, file_id, link_id, source, issued_to_user_id, reserved_slot
), released AS (
    UPDATE download_links l
       SET reserved_downloads = GREATEST(l.reserved_downloads - 1, 0),
           updated_at = now()
      FROM failed r
     WHERE r.link_id IS NOT NULL AND r.reserved_slot AND l.id = r.link_id
)
INSERT INTO download_events (file_id, link_id, grant_id, actor_type, actor_id, source, result, reason_code, bytes_sent)
SELECT file_id, link_id, id,
       CASE WHEN source = 'public_link' THEN 'anonymous' ELSE 'user' END,
       issued_to_user_id, source, 'failed', @ReasonCode, 0
  FROM failed;
