-- Persist the finished response and mark the idempotency record completed (stored result).
UPDATE idempotency_records
   SET state = 'completed',
       response_status = @Status,
       response_body = @Body::jsonb,
       response_headers = @Headers::jsonb,
       completed_at = @Now
 WHERE id = @RecordId;
