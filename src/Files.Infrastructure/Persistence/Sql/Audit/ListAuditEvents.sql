-- Unified admin/auditor read over the append-only audit trail (FR-ADM-005/008, FR-AUD-001/002).
-- file_audit_events and admin_audit_events are projected to a common shape and filtered in the
-- outer query. Keyset pagination is on (occurred_at DESC, id DESC).
SELECT
    e.id         AS "Id",
    e.category   AS "Category",
    e.file_id    AS "FileId",
    e.actor_type AS "ActorType",
    e.actor_id   AS "ActorId",
    e.action     AS "Action",
    e.result     AS "Result",
    e.reason     AS "Reason",
    e.metadata::text AS "Metadata",
    e.trace_id   AS "TraceId",
    e.occurred_at AS "OccurredAt"
FROM (
    SELECT id, 'file' AS category, file_id, actor_type, actor_id, action, result,
           reason, metadata, trace_id, occurred_at
    FROM file_audit_events
    UNION ALL
    SELECT id, 'admin' AS category,
           CASE WHEN target_type = 'file' THEN target_id ELSE NULL END AS file_id,
           CASE WHEN actor_user_id IS NULL THEN 'system' ELSE 'admin' END AS actor_type,
           actor_user_id AS actor_id, action, result, reason, metadata, trace_id, occurred_at
    FROM admin_audit_events
) e
WHERE (@Category::text IS NULL OR e.category = @Category)
  AND (@FileId::uuid IS NULL OR e.file_id = @FileId)
  AND (@ActorType::text IS NULL OR e.actor_type = @ActorType)
  AND (@Action::text IS NULL OR e.action = @Action)
  AND (@From::timestamptz IS NULL OR e.occurred_at >= @From)
  AND (@To::timestamptz IS NULL OR e.occurred_at <= @To)
  AND (@CursorOccurredAt::timestamptz IS NULL
       OR (e.occurred_at, e.id) < (@CursorOccurredAt, @CursorId::uuid))
ORDER BY e.occurred_at DESC, e.id DESC
LIMIT @Limit;
