-- Atomically claim an unexpired issued grant (issued → redeemed). Single-use (ADR-0005): the
-- state predicate guarantees at most one caller transitions the grant. `@Now` is the server clock.
-- Columns are aliased to the C# property names Dapper materializes into.
UPDATE download_grants
   SET state = 'redeemed', redeemed_at = now(), updated_at = now()
 WHERE grant_token_hash = @TokenHash
   AND state = 'issued'
   AND expires_at > @Now
RETURNING id, file_id AS FileId, source, issued_to_user_id AS IssuedToUserId;
