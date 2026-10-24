-- Append-only file audit insert. Executed through Dapper inside the ambient unit-of-work
-- transaction; `id` and `occurred_at` use database defaults.
INSERT INTO file_audit_events (file_id, actor_type, actor_id, action, result, reason, metadata, ip_hash, trace_id)
VALUES (@FileId, @ActorType, @ActorId, @Action, @Result, @Reason, @Metadata::jsonb, @IpHash, @TraceId);
