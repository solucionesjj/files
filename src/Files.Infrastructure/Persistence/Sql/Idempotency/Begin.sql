-- Insert an in-progress idempotency record, taking the (scope, key) slot if it is free. The unique
-- index makes this atomic: only one concurrent caller receives the returned id.
INSERT INTO idempotency_records (scope_type, scope_id, idempotency_key, request_hash, state, expires_at)
VALUES (@ScopeType, @ScopeId, @Key, @RequestHash, 'in_progress', @ExpiresAt)
ON CONFLICT (scope_type, scope_id, idempotency_key) DO NOTHING
RETURNING id;
