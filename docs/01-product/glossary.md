# Glossary

| Term | Definition |
|---|---|
| **Files** | The product described here: secure file storage, transfer, and integration platform. |
| **Portal** | Angular web application for human users, authenticated with Microsoft Entra ID. |
| **API** | Versioned REST API for integrations and for the Portal, served by the .NET backend. |
| **Worker** | Background process that runs scheduled jobs (notifications, expiration, deletion, reconciliation, outbox). |
| **Integration** | Registered machine consumer with an identity, permissions, status, credentials, and ownership boundary. |
| **Integration credential** | Entra application identity (client ID + consumer-owned secret/certificate) used by an integration to authenticate. |
| **API key** | Not used in v1; retained only as a historical term. Machine authentication is Entra client credentials. |
| **File** | A stored object plus its metadata record. Identified by a UUIDv7 file ID. |
| **Storage key** | Internal, system-generated S3 object key. Never exposed to clients; never derived from the filename. |
| **Owner** | The human user or integration that created and owns a file. |
| **Access mode** | Maximum exposure currently granted to a file: `owner_only`, `private_link`, `public_link`, `integration_only`. |
| **Download link** | Shareable reference to a file, identified by an unguessable 256-bit token, with type, expiry, limits, and status. |
| **Link token** | The unguessable part of a link URL (`/d/{token}`), 32 random bytes base64url-encoded. |
| **Download key** | Eight-character case-sensitive key required in addition to a public link token. Never in the URL; stored as an Argon2id verifier. |
| **Grant** | Short-lived (120 s), single-use authorization to stream a specific file, issued after successful link validation. |
| **Reservation** | A grant that holds one slot against a link's `max_downloads` until it completes or expires. |
| **Successful download** | A download whose response stream completed. Only these increment `successful_download_count`. |
| **Retention** | Policy that determines when a file expires: finite (`expires_at`) or indefinite (null expiry). |
| **Expiration** | Logical moment after which a file is no longer downloadable, regardless of physical deletion. |
| **Pending deletion** | State after expiration/deletion request, before S3 deletion succeeds. Not downloadable. |
| **Tombstone** | Retained file metadata row after physical deletion, kept for audit for a configured period. |
| **Quarantine** | State for files that failed malware scanning or policy checks; never downloadable. |
| **Audit event** | Append-only record of an action (actor, action, target, time, result, metadata). |
| **Download event** | Record of a download attempt or grant outcome, including denied attempts. |
| **Idempotency key** | Client-supplied key that makes a mutating request safely retryable and returns the stored result. |
| **Outbox** | Table of domain events committed with the transaction, dispatched asynchronously to jobs/emails. |
| **SLO** | Service-level objective; an internal target derived from NFRs (e.g., 99.9% availability). |
| **RPO / RTO** | Recovery point objective (max data loss) / recovery time objective (max downtime). |
| **Presigned URL** | Time-limited S3 URL that authorizes one operation without exposing AWS credentials. **Not used by Files**; S3 is exclusive to the API (D-30). |
| **Multipart upload** | S3 mechanism for uploading large objects in parts and completing atomically. |
| **Staging file** | Temporary local file on the EC2 EBS volume where the API writes upload chunks before committing them to S3. |
| **Chunk** | Fixed-size slice of an upload (16 MiB default) sent to the API and written at an offset in the staging file. |
| **Problem Details** | RFC 9457 JSON error format used by the API (`application/problem+json`). |
| **Cursor pagination** | Opaque, stable pagination based on a sort key rather than offsets. |
