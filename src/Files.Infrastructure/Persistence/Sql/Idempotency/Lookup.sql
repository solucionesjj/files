-- Read the current idempotency record for classification (replay / reuse / in-progress).
SELECT id, request_hash AS RequestHash, state, response_status AS ResponseStatus,
       response_body AS ResponseBody, response_headers AS ResponseHeaders, expires_at AS ExpiresAt
  FROM idempotency_records
 WHERE scope_type = @ScopeType AND scope_id = @ScopeId AND idempotency_key = @Key;
