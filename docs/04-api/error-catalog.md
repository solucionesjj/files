# Error Catalog

All errors use `application/problem+json` (RFC 9457) with `code`, `traceId`, and `retryable`.
Codes are stable; clients must branch on `code`, not on `title` or `detail`.

**Generic rule:** for link/grant/foreign-resource lookups the response is the same regardless of
which condition failed. Specific reasons are recorded only in audit events and metrics.

## Authentication and authorization

| Code | HTTP | Meaning | Retryable | Client action |
|---|---|---|---|---|
| `invalid_token` | 401 | Missing, malformed, expired, wrong audience/issuer, or wrong tenant. | No | Acquire a new token. |
| `token_expired` | 401 | Token `exp` passed. | No | Renew silently, retry once. |
| `tenant_not_allowed` | 401 | Token `tid` differs from the configured tenant. | No | Use the correct account. |
| `api_key_invalid` | 401 | Unknown key ID or verifier mismatch. | No | Check configuration; rotate. |
| `api_key_expired` | 401 | Key past `expires_at` or revoked. | No | Use the rotated key. |
| `integration_disabled` | 401 | Integration status is `disabled`. | No | Contact the administrator. |
| `ip_not_allowed` | 403 | Source IP outside the integration allowlist. | No | Use the approved network. |
| `forbidden` | 403 | Authenticated but lacking the role/permission. | No | Request access. |
| `insufficient_permission` | 403 | Integration lacks the required permission for this operation. | No | Request the permission. |
| `public_links_disabled` | 403 | Public links are disabled by policy. | No | Use a private link. |
| `rate_limited` | 429 | Rate limit exceeded. | Yes | Honor `Retry-After`. |
| `quota_exceeded` | 429 | Daily/integration quota exhausted. | Yes (later) | Wait for the next day or request a quota increase. |
| `too_many_attempts` | 429 | Link key attempts locked out. | Yes | Wait for `Retry-After`. |

## Files and uploads

| Code | HTTP | Meaning | Retryable | Client action |
|---|---|---|---|---|
| `file_not_found` | 404 | No file or no access (indistinguishable). | No | Verify the ID. |
| `file_unavailable` | 404 | Exists but not downloadable (processing, quarantine, missing object). | No | Check availability later. |
| `file_expired` | 404 | Logical expiry passed (internal reason; public code is `file_unavailable`). | No | Extend retention before expiry. |
| `file_quarantined` | 404 | Malware/policy quarantine (internal reason; public code `file_unavailable`). | No | Contact the administrator. |
| `file_too_large` | 422 | Declared/actual size exceeds the configured maximum. | No | Split or compress the file. |
| `unsupported_content_type` | 422 | Content type outside the allowlist. | No | Use an allowed type. |
| `invalid_filename` | 422 | Empty, too long, or contains forbidden characters. | No | Sanitize the name. |
| `stale_version` | 412 | `If-Match` does not match the current version. | No | Re-read and retry. |
| `retention_exceeds_maximum` | 422 | Requested retention > policy maximum. | No | Choose a shorter retention. |
| `retention_not_allowed` | 422 | Mode (indefinite) not permitted for this owner. | No | Choose finite retention. |
| `insufficient_storage` | 507 | Deployment storage quota exhausted. | Yes (admin) | Free space / raise quota. |
| `upload_session_not_found` | 404 | Unknown session or not owned by caller. | No | Create a new session. |
| `upload_session_expired` | 410 | Session TTL passed. | No | Create a new session. |
| `upload_session_completed` | 409 | Session already completed. | No | Use the returned file. |
| `upload_session_aborted` | 409 | Session aborted. | No | Create a new session. |
| `upload_parts_incomplete` | 422 | S3 reports missing parts. | Yes | Upload missing parts and retry complete. |
| `upload_size_mismatch` | 422 | Actual object size ≠ declared size. | No | Re-upload with the correct size. |
| `upload_checksum_mismatch` | 422 | SHA-256 mismatch. | No | Re-upload. |
| `presign_limit_exceeded` | 422 | Too many part numbers in one request (>100). | No | Split the request. |
| `upload_abort_failed` | 502 | S3 abort failed; retried by cleanup. | Yes | Retry later. |

## Links, keys, grants

| Code | HTTP | Meaning | Retryable | Client action |
|---|---|---|---|---|
| `link_not_found` | 404 | Invalid token, revoked, expired, file unavailable, wrong key, or limit reached. | No | Check link/key; request a new link. |
| `link_key_required` | 400 | Public link redeemed without `key`. | No | Supply the key. |
| `grant_not_found` | 404 | Unknown, expired, consumed, or invalidated grant. | No | Request a new grant. |
| `grant_expired` | 410 | Grant TTL passed. | No | Request a new grant. |
| `grant_consumed` | 409 | Grant already used. | No | Request a new grant. |
| `download_limit_reached` | 404 | Max downloads reached (public code `link_not_found`). | No | Ask the owner for a new link. |

## Request and system

| Code | HTTP | Meaning | Retryable | Client action |
|---|---|---|---|---|
| `validation_error` | 400 | Shape/range/type validation failed; see `errors`. | No | Fix the payload. |
| `malformed_request` | 400 | Invalid JSON/encoding. | No | Fix the payload. |
| `invalid_query_parameter` | 400 | Unknown/invalid filter, sort, or limit. | No | Use documented values. |
| `invalid_cursor` | 400 | Cursor malformed/expired. | No | Restart pagination. |
| `unsupported_media_type` | 415 | Wrong `Content-Type`. | No | Use the documented type. |
| `payload_too_large` | 413 | Body/size exceeds the endpoint limit. | No | Reduce the payload. |
| `not_found` | 404 | Unknown route. | No | Check the path. |
| `method_not_allowed` | 405 | Wrong method. | No | Check the contract. |
| `conflict` | 409 | State conflict not covered by a specific code. | No | Re-read state. |
| `idempotency_key_reuse` | 422 | Same key with a different request body. | No | Use a new key. |
| `idempotency_in_progress` | 409 | Original request still running. | Yes | Retry after `Retry-After`. |
| `integration_slug_taken` | 409 | Slug already exists. | No | Choose another. |
| `integration_not_found` | 404 | Unknown integration. | No | Check the ID. |
| `credential_not_found` | 404 | Unknown credential. | No | List credentials. |
| `credential_already_revoked` | 409 | Revocation repeated. | No | — |
| `policy_not_found` | 404 | Unknown policy version. | No | Read current policy. |
| `reason_required` | 422 | Admin action requires a reason (≥10 chars). | No | Supply a reason. |
| `dependency_unavailable` | 502/503 | S3/Graph/DB unavailable. | Yes | Retry with backoff. |
| `service_unavailable` | 503 | Maintenance/circuit open. | Yes | Honor `Retry-After`. |
| `internal_error` | 500 | Unexpected error. | Maybe | Contact support with `traceId`. |

## Implementation notes

- `ProblemDetails` is produced by a single exception handler and result mapper; endpoints never
  hand-craft error JSON.
- `detail` is human-readable and safe; it never contains SQL, S3 keys, tokens, emails of other
  users, or stack traces.
- 404 vs 403: foreign/invalid resources return 404; admin routes may return 403 because the
  caller is already in the admin namespace and existence is not sensitive.
- Every 5xx logs the full exception with `traceId`; clients get the same ID for support.
