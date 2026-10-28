INSERT INTO upload_chunks (upload_session_id, chunk_number, offset_bytes, size_bytes, checksum_sha256)
VALUES (@SessionId, @ChunkNumber, @OffsetBytes, @SizeBytes, @ChecksumSha256)
ON CONFLICT (upload_session_id, chunk_number) DO NOTHING
RETURNING chunk_number;
