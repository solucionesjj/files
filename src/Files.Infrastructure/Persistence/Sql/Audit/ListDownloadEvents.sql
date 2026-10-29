-- Admin/auditor read over download_events (FR-ADM-005/008, FR-AUD-001).
-- Keyset pagination is on (occurred_at DESC, id DESC).
SELECT
    id          AS "Id",
    file_id     AS "FileId",
    link_id     AS "LinkId",
    actor_type  AS "ActorType",
    actor_id    AS "ActorId",
    source      AS "Source",
    result      AS "Result",
    reason_code AS "ReasonCode",
    bytes_sent  AS "BytesSent",
    trace_id    AS "TraceId",
    occurred_at AS "OccurredAt"
FROM download_events
WHERE (@FileId::uuid IS NULL OR file_id = @FileId)
  AND (@LinkId::uuid IS NULL OR link_id = @LinkId)
  AND (@Result::text IS NULL OR result = @Result)
  AND (@From::timestamptz IS NULL OR occurred_at >= @From)
  AND (@To::timestamptz IS NULL OR occurred_at <= @To)
  AND (@CursorOccurredAt::timestamptz IS NULL
       OR (occurred_at, id) < (@CursorOccurredAt, @CursorId::uuid))
ORDER BY occurred_at DESC, id DESC
LIMIT @Limit;
