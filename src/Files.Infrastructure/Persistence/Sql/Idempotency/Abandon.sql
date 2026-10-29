-- Remove an in-progress record after a 5xx so the client can retry with the same key.
DELETE FROM idempotency_records
 WHERE scope_type = @ScopeType AND scope_id = @ScopeId AND idempotency_key = @Key AND state = 'in_progress';
