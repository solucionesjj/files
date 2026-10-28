UPDATE upload_sessions
   SET received_bytes = received_bytes + @SizeBytes,
       status = CASE WHEN received_bytes + @SizeBytes = expected_size_bytes THEN 'received' ELSE 'uploading' END
 WHERE id = @SessionId AND status IN ('initiated', 'uploading')
RETURNING received_bytes AS ReceivedBytes, status AS Status;
