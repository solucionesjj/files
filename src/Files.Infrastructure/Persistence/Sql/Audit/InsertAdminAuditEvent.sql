-- Append-only admin audit insert. Executed through Dapper inside the ambient unit-of-work
-- transaction; `id` and `occurred_at` use database defaults.
INSERT INTO admin_audit_events (actor_user_id, actor_role, action, target_type, target_id, result, reason, metadata, ip_hash, trace_id)
VALUES (@ActorUserId, @ActorRole, @Action, @TargetType, @TargetId, @Result, @Reason, @Metadata::jsonb, @IpHash, @TraceId);
