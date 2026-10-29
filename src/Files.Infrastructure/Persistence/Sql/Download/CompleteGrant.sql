-- Complete a redeemed grant in one transaction: mark it completed, increment the file's download
-- counter exactly once (the `state = 'redeemed'` predicate makes a repeat a no-op), convert the
-- grant's reserved link slot into a success (reserved → successful), and append the `completed`
-- download event. A grant is one counted download (ADR-0005).
WITH redeemed AS (
    UPDATE download_grants
       SET state = 'completed', completed_at = now(), bytes_sent = @BytesSent, updated_at = now()
     WHERE id = @GrantId AND state = 'redeemed'
     RETURNING id, file_id, link_id, source, issued_to_user_id, reserved_slot
), bumped AS (
    UPDATE files f
       SET successful_downloads = successful_downloads + 1,
           last_download_at = now()
      FROM redeemed r
     WHERE f.id = r.file_id
), converted AS (
    UPDATE download_links l
       SET successful_downloads = l.successful_downloads + 1,
           reserved_downloads = GREATEST(l.reserved_downloads - 1, 0),
           updated_at = now()
      FROM redeemed r
     WHERE r.link_id IS NOT NULL AND r.reserved_slot AND l.id = r.link_id
)
INSERT INTO download_events (file_id, link_id, grant_id, actor_type, actor_id, source, result, bytes_sent)
SELECT file_id, link_id, id,
       CASE WHEN source = 'public_link' THEN 'anonymous' ELSE 'user' END,
       issued_to_user_id, source, 'completed', @BytesSent
  FROM redeemed;
