SELECT f.id AS FileId, s.id AS SessionId
  FROM files f
  JOIN upload_sessions s ON s.file_id = f.id
 WHERE f.status = 'processing'
   AND f.scan_status IN ('pending', 'failed')
   AND (f.scan_next_attempt_at IS NULL OR f.scan_next_attempt_at <= now())
   AND s.status = 'committing'
 ORDER BY f.created_at
 LIMIT @Limit
 FOR UPDATE OF f SKIP LOCKED;
