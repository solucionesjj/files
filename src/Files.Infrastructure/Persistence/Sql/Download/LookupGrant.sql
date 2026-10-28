-- Read a grant's current state to classify a failed claim (no transaction required).
SELECT state
  FROM download_grants
 WHERE grant_token_hash = @TokenHash;
