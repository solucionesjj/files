# FILES — Product Requirements and Technical Specification

**Version:** 0.2  
**Status:** Draft for architecture and requirements review  
**Product type:** Secure file storage, transfer, and integration platform

## 1. Product Vision

Files is a centralized service for storing, managing, and distributing files securely through a web portal and a programmatic API.

The platform must support two primary use cases:

1. Human users upload, organize, retain, and share files through a web application.
2. Authorized enterprise systems upload and retrieve files through an API, without storing file contents in their own application repositories.

The platform must centralize file storage, access control, retention, deletion, download authorization, and auditability.

The solution must use Amazon S3 for private object storage and must be designed for secure, cost-efficient, and maintainable operation.

## 2. Business Context

An existing enterprise product allows users to attach files to customer records and download those files on demand.

Currently, file contents are stored within the existing product's repositories or storage infrastructure.

The new Files platform will externalize file storage and expose controlled operations for uploading and retrieving files.

The existing product will retain its business records and store a reference to each file in Files instead of storing the file contents locally.

The integration must preserve the relationship between each file and the corresponding customer record in the existing product.

Files must not require access to the complete customer database of the consuming system. The consuming system remains responsible for its business data and for deciding which authorized business users may request a file.

## 3. Product Components

### 3.1. Files Portal

A web application for human users, authenticated through Microsoft Entra ID.

Users can upload files, search and organize their files, configure retention within authorized limits, manage download links, and review download statistics.

### 3.2. Files API

A secure API for authorized enterprise applications.

The API must support:
- Creating files on behalf of an authorized integration.
- Retrieving file metadata.
- Downloading file contents.
- Deleting or managing files when explicitly permitted.
- Checking file availability.
- Applying integration-specific authorization policies.

Machine-to-machine operations must not depend on an interactive user session.

### 3.3. Files Administration

An administrative interface for global policies, integrations, storage limits, notification settings, security configuration, and audit events.

## 4. Identity and Access Models

Files must distinguish human identities from machine identities.

### 4.1. Human Identity

- Authenticate users through Microsoft Entra ID.
- Identify each user using the Entra tenant ID and object ID.
- Enforce authorization in the backend.
- Associate user-created files with their owner.
- Restrict access to other users' files unless explicitly authorized.
- Allow private links for authenticated users belonging to the same configured Entra tenant.

### 4.2. Machine Identity

Each consuming application must have a registered integration identity.

An integration must have:
- A unique integration identifier.
- A descriptive name.
- An active or disabled status.
- Its authentication configuration.
- Explicitly granted permissions.
- Access restrictions.
- Creation and rotation metadata for its credentials.
- Audit history.

Machine identities must be isolated from human user identities.

An integration must only access files that it owns or has explicitly been authorized to access.

### 4.3. API Authentication

The preferred authentication mechanism for enterprise integrations is Microsoft Entra ID OAuth 2.0 client credentials, using application identities and application permissions.

If API keys are required for compatibility with existing products, implement them as high-entropy credentials associated with an integration.

API key requirements:
- Generate keys securely.
- Store only a cryptographic hash or equivalent non-reversible verifier.
- Display the secret only when it is initially issued.
- Support revocation, rotation, expiration, and overlapping credentials during rotation.
- Associate every key with a specific integration and defined permissions.
- Never expose credentials in URLs, logs, or error messages.
- Apply rate limits and monitoring.
- Require TLS for all requests.

An API key must not act as a universal credential that grants unrestricted access to Files.

## 5. File Categories and Access Policies

Files must support at least the following access models.

### 5.1. User-Owned Files

Files uploaded by a human user through the portal.

The owner can manage metadata, tags, retention, and download links, subject to administrative policies.

### 5.2. Public-Link Files

Files made available through a public download link.

Anyone possessing a valid link and its associated download key may download the file without an interactive Entra ID login.

The file remains private in Amazon S3. Public access must be mediated by Files.

### 5.3. Private-Link Files

Files available through links requiring authentication.

The initial default policy is to permit authenticated users belonging to the configured Entra tenant, subject to authorization checks.

The system must not interpret tenant membership alone as authorization to access every file. File ownership, link validity, and applicable permissions must also be evaluated.

### 5.4. Integration-Owned Files

Files created by an authorized machine integration.

Default behavior:
- Assign the file to the integration that created it.
- Apply indefinite retention.
- Do not generate a public download link automatically.
- Do not allow human portal users to download the file unless an explicit authorization policy permits it.
- Permit retrieval only by the owning integration or another explicitly authorized integration.
- Record every authorized retrieval and every rejected access attempt.

Integration-owned files must remain private regardless of their retention policy.

Indefinite retention must not automatically override legal requirements, administrator restrictions, or explicitly authorized deletion operations.

## 6. Public Download Keys

Every public download link must have an associated download key.

The key must:
- Contain exactly eight characters.
- Use uppercase letters, lowercase letters, and digits.
- Be generated using a cryptographically secure random number generator.
- Be case-sensitive.
- Be verified by the backend before the file is made available.
- Never be embedded in the public URL.
- Never be written to application logs.
- Be stored as a secure verifier rather than plaintext.
- Be changeable or replaceable by revoking the existing link and creating a new one.

The key must be entered separately from the download link.

The download process must:
1. Receive the public link identifier or token.
2. Validate that the link exists and is active.
3. Validate that the associated file is available.
4. Check link expiration and download limits.
5. Verify the supplied download key.
6. Apply rate limits and abuse detection.
7. Authorize the download.
8. Record the result.
9. Return the file or a short-lived signed download URL.

The backend must not reveal whether a link, file, or key was correct through unnecessarily detailed error responses.

Because an eight-character alphanumeric key has limited entropy, enforce attempt throttling, progressive delays or temporary blocking, and monitoring of repeated failures.

The download key is an additional access control, not a replacement for secure token generation, TLS, authorization, or rate limiting.

## 7. File Upload and Storage

Files must store binary content in a private Amazon S3 bucket.

The relational database must contain file metadata and storage references, not the full file contents.

For initial planning, support files up to 5 GB, with the actual limit configurable.

Large uploads must use Amazon S3 multipart upload or another supported direct-upload mechanism.

Requirements:
- Generate unique object keys.
- Never use the original filename as the storage key.
- Validate file size and permitted content types.
- Validate the upload authorization before issuing upload permissions.
- Use short-lived, restricted upload URLs or equivalent mechanisms.
- Validate that the uploaded object matches the expected size and upload state.
- Abort incomplete multipart uploads after a configurable period.
- Prevent unauthorized object overwrite.
- Encrypt objects at rest.
- Use TLS for data transfers.
- Keep S3 Block Public Access enabled.
- Configure lifecycle and cleanup rules for incomplete uploads and orphaned objects.

The original filename must be stored as metadata and sanitized when displayed or downloaded.

## 8. File Metadata

Each file record must include, at minimum:

- File ID.
- Original filename.
- Internal storage key.
- Object storage provider and bucket reference.
- File size.
- Verified content type.
- File owner type: human or integration.
- Human owner ID or integration ID.
- Creation timestamp.
- Last modification timestamp.
- Retention mode.
- Effective expiration timestamp, when applicable.
- File lifecycle status.
- Tag associations.
- Successful download count.
- Last successful download timestamp.
- Malware scanning status, when scanning is enabled.
- Deletion and audit metadata.

Sensitive storage references must not be exposed to unauthorized clients.

## 9. Retention Policies

The system administrator must configure:
- Default retention duration for human-uploaded files.
- Whether users can customize retention.
- The maximum retention duration, if applicable.
- Whether indefinite retention is permitted.
- Notification intervals before expiration.
- Administrative override permissions.

When a human uploads a file, the system applies the current global default.

Users may change the retention policy if authorized.

A change to the global default affects newly created files by default.

Existing files retain their current expiration dates unless an administrator explicitly executes a bulk policy migration.

Integration-owned files receive indefinite retention by default.

The system must distinguish between:
- Finite retention with an expiration date.
- Indefinite retention without a scheduled expiration.

Indefinite retention must not be represented by an arbitrary far-future timestamp.

## 10. Expiration Notifications

Administrators must be able to configure multiple notification intervals.

Example:
- Seven days before expiration.
- Three days before expiration.
- One day before expiration.

The system must send notifications to the file owner's registered email address.

Notification requirements:
- Use configurable email templates.
- Include relevant file information and expiration date.
- Avoid exposing sensitive file contents or access credentials.
- Track notification scheduling and delivery attempts.
- Avoid duplicate notifications for the same expiration schedule.
- Recalculate pending notifications when retention changes.
- Retry transient delivery failures.
- Suppress expiration notifications for indefinite-retention files.

Email delivery acceptance by the provider must be distinguished from confirmed delivery to the recipient.

## 11. Expiration and Deletion Lifecycle

The lifecycle process must:
1. Identify files approaching expiration.
2. Send due notifications.
3. Identify files whose effective expiration time has passed.
4. Prevent further downloads of expired files.
5. Transition the file to an expiration or pending-deletion state.
6. Delete the object from S3.
7. Record the result.
8. Retry recoverable failures.
9. Verify and reconcile inconsistent storage and database states.

The process must be idempotent and safe to run repeatedly.

Expired files must not remain downloadable while awaiting physical deletion.

The system must distinguish logical expiration, successful physical deletion, and recoverable deletion failure.

Backup retention and replicas must be addressed separately so that the product does not claim complete physical erasure while copies remain recoverable under backup policies.

## 12. Download Links

Each link must have:
- A unique identifier.
- A secure, unguessable token.
- A file reference.
- A link type.
- A creation timestamp.
- An optional expiration timestamp.
- An active, revoked, or expired status.
- An optional maximum successful download count.
- A successful download counter.
- A creator identity.
- A download authorization policy.

Public links require a download key.

Private links require Entra ID authentication and applicable authorization.

The validity of a download link must be independent of file retention, but a link must never permit downloading a file that has expired, been deleted, or become unavailable.

The system must define whether concurrent download requests reserve or consume download-count limits.

Download counts must follow a documented rule and must not be incremented merely because a download URL was generated.

## 13. Tags, Search, and Sorting

Human users must be able to:
- Assign multiple tags to files.
- Reuse existing tags.
- Remove tags.
- Search filenames case-insensitively.
- Filter by one or more tags.
- Filter by file status and expiration period.
- Sort by filename, creation date, and successful download count.
- Choose ascending or descending order.
- Navigate results using server-side pagination.

Search and listing operations must enforce ownership and authorization before returning results.

Integration-owned files must not be exposed through ordinary user searches unless explicitly authorized.

## 14. Integration API

Provide versioned API endpoints for machine integrations.

Proposed operations:

- Create an upload session.
- Complete or confirm an upload.
- Retrieve file metadata.
- Download file contents.
- Check file availability.
- Delete or revoke file access, when explicitly authorized.

Every request must authenticate the integration and verify its permissions.

File creation through the integration API must:
- Associate the file with the calling integration.
- Apply indefinite retention by default.
- Set the access mode to integration-only.
- Avoid creating public links.
- Return a stable file ID and the necessary upload instructions.

File retrieval must verify that the requesting integration owns the file or has explicit access.

The API must not expose S3 credentials, unrestricted bucket access, or permanent public URLs.

The API must support idempotency for operations where retries could otherwise create duplicate files or duplicate business actions.

## 15. Integration with the Existing Enterprise Product

The existing product must continue to manage customer records and business authorization.

Files must manage file storage and file-level access control.

The recommended flow is:

1. An authorized user selects a file associated with a customer record in the existing product.
2. The existing product backend authenticates to Files as a registered integration.
3. Files creates an upload session associated with that integration.
4. The existing product uploads the file directly to the Files API.
5. Files upload the file into a temporal storage and then upload it to S3 storage
6. Files verifies the completed upload and stores its metadata.
7. Files returns a stable file ID.
8. The existing product stores the file ID alongside the relevant customer record.
9. When the user requests a download, the existing product backend checks the user's business permissions.
10. The existing product backend requests the file from Files using its integration identity.
11. Files verifies the integration's file-level authorization and returns the content through an authorized download flow.

The integration must never send its API key or client secret to the browser.

The existing product must not trust a file ID supplied by the browser without performing its own business authorization checks.

The Files API must independently enforce the integration's access restrictions.

## 16. Proposed Architecture

- Frontend: Angular with standard latest Bootstrap Framework (https://getbootstrap.com/).
- Identity: Microsoft Entra ID.
- API: C# and .NET.
- Container first
- File storage: Private Amazon S3.
- Large-file transfers: S3 multipart upload and restricted, short-lived presigned URLs.
- Database: PostgreSQL 18 LTS
- Email: MS365 Pop account
- Secrets: on enviroment files by the moment
- Monitoring: SigNoz open source telemetry
- Environments: Development, testing, and production.

The architecture must separate control-plane operations, such as authentication, authorization, metadata, and policy enforcement, from the transfer of large file contents.

## 17. Proposed Data Model

### Users
Human identities mapped to Entra tenant and object IDs.

### Integrations
Registered machine consumers with identity configuration, status, permissions, and ownership boundaries.

### IntegrationCredentials
Credential verifiers, credential type, creation and expiration metadata, rotation status, and revocation information.

### Files
File metadata, owner type, owner reference, storage reference, size, content type, lifecycle status, retention policy, and download statistics.

### Tags
Reusable tag definitions with appropriate ownership or scope.

### FileTags
Many-to-many association between files and tags.

### DownloadLinks
Link token verifier, file ID, access mode, expiration, status, download key verifier for public links, and download limits.

### DownloadEvents
Audit records for download attempts, authorization results, timestamps, and relevant link or integration identifiers.

### RetentionPolicies
Global defaults and applicable policy settings.

### NotificationPolicies
Configurable notification intervals and email template references.

### NotificationEvents
File ID, expiration schedule identifier, reminder interval, delivery attempts, and result.

### FileAuditEvents
Records of uploads, metadata changes, retention modifications, access changes, deletions, and administrative actions.

### UploadSessions
Authorized upload sessions, expected size, expiration, completion state, and multipart-upload references.

### IdempotencyRecords
Keys and results used to safely process retried API operations.

The database design must define primary keys, foreign keys, indexes, uniqueness constraints, retention requirements, and transactional boundaries.

## 18. Non-Functional Requirements

- Enforce strict user and integration isolation.
- Ensure idempotent background processing.
- Provide retry and recovery mechanisms.
- Apply least-privilege IAM policies.
- Prevent unauthorized public storage access.
- Provide monitoring and operational alerts.
- Maintain auditable access and deletion events.
- Use configurable quotas and limits.
- Support automated tests and reproducible deployments.
- Define backup and disaster recovery objectives.
- Define data retention requirements for audit logs and backups.
- Provide cost visibility for storage, requests, transfers, and background processing.

## 19. Required Deliverables Before Development

Before implementation, produce:

1. Requirements specification and unresolved questions.
2. User stories and acceptance criteria.
3. Architecture diagrams.
4. Data model and database migrations.
5. API contracts and OpenAPI documentation.
6. Entra ID authentication design.
7. Machine-to-machine authorization design.
8. Threat model and security requirements.
9. File lifecycle state-transition model.
10. Large-file upload and download design.
11. Retention, notification, and deletion workflow.
12. Automated testing strategy.
13. Infrastructure as code.
14. Deployment and rollback procedures.
15. Cost model and operational monitoring plan.

Implementation must proceed incrementally after critical design decisions have been reviewed.
