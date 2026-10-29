# User Stories and Acceptance Criteria

Format: story → priority → milestone → requirements. Acceptance criteria are written as
Gherkin and are the source for automated E2E/integration tests. Story IDs are stable.

Milestones are defined in [roadmap.md](../09-delivery/roadmap.md).

---

## Epic E1 — Identity and access

### US-001 — Sign in with Microsoft Entra ID
**As** a tenant employee **I want** to sign in with my corporate account **so that** I can manage
my files without another password.
**Priority** M · **Milestone** M1 · **Requirements** FR-ID-001..004, 008, 009

```gherkin
Scenario: First successful sign-in provisions the user
  Given I am a member of the configured Entra tenant
  When I sign in through the portal with a valid token
  Then a user record exists for my tenant and object ID
  And I can see the file list page
  And no password is stored by Files

Scenario: A token from another tenant is rejected
  Given I have a valid token issued by a different Entra tenant
  When I call GET /v1/me
  Then the response is 401 with code "invalid_token"
  And no user record is created

Scenario: Expired or tampered token is rejected
  Given my access token is expired or its signature is invalid
  When I call any protected endpoint
  Then the response is 401
  And the response contains a trace ID but no token details
```

### US-002 — Role-based administration
**As** a Files administrator **I want** admin pages protected by my Files role **so that** regular
users cannot change global policy.
**Priority** M · **Milestone** M1 · **Requirements** FR-ID-010, 011, FR-ADM-001..005

```gherkin
Scenario: Admin role grants administration
  Given my user has the "admin" role in Files
  When I open /admin
  Then I can see integrations, API keys, policies, and audit sections

Scenario: Regular user is denied
  Given my user has the "user" role in Files
  When I call GET /v1/admin/integrations
  Then the response is 403 with code "forbidden"
  And the response does not include integration data
```

---

## Epic E2 — Human file management

### US-003 — Upload a file through the Files API
**As** a portal user **I want** to upload a file in chunks with progress and validation **so that**
it is securely staged, stored, and available for sharing without my browser touching S3.
**Priority** M · **Milestone** M2/M3 · **Requirements** FR-UP-001..007, 009, 011, 012, 014..018, FR-RET-002

```gherkin
Scenario: Small file upload completes and becomes available
  Given I selected a 10 MiB PDF within the allowed size
  When I confirm the upload
  Then Files creates an upload session and a pending file owned by me
  And the session returns a chunk size and chunk count, never an S3 URL or credential
  When I upload all chunks and call complete
  Then the API verifies the staged bytes and uploads the object to S3 itself
  And the object is stored under a generated key with no filename in it
  And the file becomes "available" after size and content-type verification
  And the file list shows it with the global default retention

Scenario: File too large is rejected before upload
  Given the configured maximum is 5 GiB
  When I select a 6 GiB file
  Then the portal shows a validation error
  And no upload session is created

Scenario: Disallowed content type is rejected
  Given the allowlist excludes executable files
  When I request an upload session for "setup.exe"
  Then the response is 422 with code "unsupported_content_type"
  And no staging file is created

Scenario: Insufficient staging disk
  Given the staging reserve would be exhausted by a new 5 GiB session
  When I request the session
  Then the response is 507 with code "staging_capacity_exceeded"
  And no temp file is created

Scenario: Upload cancelled or expired
  Given I started a chunked upload and stopped
  When I abort the session or it expires
  Then the staging file is deleted
  And any S3 multipart upload is aborted
  And the file is not listed as available

Scenario: Chunk retry is idempotent
  Given I uploaded chunk 3 successfully
  When my client retries chunk 3 with the same content
  Then the API returns success without duplicating bytes
  When I retry chunk 3 with different content
  Then the response is 409 with code "chunk_conflict"

Scenario: Infected file never reaches S3
  Given ClamAV is enabled and detects malware in the staged file
  When the scan/commit job runs
  Then the file becomes "quarantined"
  And no object is created in S3
  And the staging file is deleted
  And an alert and audit event are recorded
```

### US-004 — Browse and inspect my files
**As** a portal user **I want** to see my files with key metadata **so that** I know what I have
and when it expires.
**Priority** M · **Milestone** M3 · **Requirements** FR-ID-006, FR-FILE-001/002, FR-RET-004

```gherkin
Scenario: I only see my own files
  Given another user owns a file
  When I list my files
  Then their file does not appear
  And the total count does not include it

Scenario: File detail shows retention correctly
  Given a human file expires on 2027-01-31
  And an integration file has indefinite retention
  When I open their detail pages
  Then the human file shows the exact expiry date
  And the integration file shows "Indefinite" without a fabricated date
```

### US-005 — Edit metadata and retention
**As** a file owner **I want** to rename a file, change tags, and adjust retention within limits
**so that** my file stays accurate and compliant.
**Priority** M · **Milestone** M3 · **Requirements** FR-FILE-003/004, FR-RET-003/006

```gherkin
Scenario: Owner extends retention within policy with a mandatory note
  Given the current default is 30 days and the maximum is 365 days
  When I extend my file to 90 days and write "contract review until Q3"
  Then the file is updated and a file audit event "retention_changed" is recorded with the note
  And pending expiration notifications are rescheduled

Scenario: Extending without a note is rejected
  Given the current default is 30 days
  When I request 60 days without a reason
  Then the response is 422 with code "retention_reason_required"
  And the file's expiry is unchanged

Scenario: Retention beyond 365 days is rejected
  Given the absolute maximum is 365 days
  When I request an expiry 400 days from now
  Then the response is 422 with code "retention_exceeds_maximum"
  And the file's expiry is unchanged

Scenario: Concurrent edit conflict is detected
  Given another tab updated the file after I loaded it
  When I save my changes with the stale version
  Then the response is 412 with code "stale_version"
  And the portal asks me to reload
```

### US-006 — Download my own file
**As** a file owner **I want** to download my file through the portal **so that** I can retrieve
the content later.
**Priority** M · **Milestone** M3 · **Requirements** FR-ID-006, FR-FILE-002, FR-LINK-010/011, D-21

```gherkin
Scenario: Owner download succeeds and is counted
  Given my file is available and not expired
  When I download it
  Then the bytes are streamed with the sanitized original filename
  And successful_download_count increments by one
  And a download event with source "owner" is recorded

Scenario: Expired file cannot be downloaded
  Given my file's expiry passed one minute ago
  When I request its content
  Then the response is 404 with code "file_unavailable"
  And no bytes are served
```

### US-007 — Manage tags
**As** a portal user **I want** to reuse and assign tags **so that** I can organize files.
**Priority** M · **Milestone** M3 · **Requirements** FR-SRCH-001

```gherkin
Scenario: Reuse an existing tag
  Given the tag "contracts" already exists in my scope
  When I add "contracts" to a file
  Then no duplicate tag is created
  And the file shows the tag

Scenario: Tags are case-insensitive for uniqueness
  Given the tag "Contracts" exists
  When I create "contracts"
  Then the existing tag is reused
  And the display name remains as first created
```

### US-008 — Search, filter, sort, paginate
**As** a portal user **I want** to find files quickly **so that** I don't scroll through everything.
**Priority** M · **Milestone** M3 · **Requirements** FR-SRCH-002..006

```gherkin
Scenario: Case-insensitive partial filename search
  Given I own "Q3 Financial Report.PDF"
  When I search "financial"
  Then the file appears

Scenario: Filter by tag and status
  Given I own files tagged "legal" that are available and expired
  When I filter by tag "legal" and status "available"
  Then only the available tagged file appears

Scenario: Stable cursor pagination
  Given I own 30 files sorted by creation date descending
  When I request the first page with limit 25 and follow the cursor
  Then I receive the remaining 5 files exactly once
  And the cursor is opaque and does not expose SQL

Scenario: Sorting by download count
  Given my files have different successful download counts
  When I sort by "-downloads"
  Then the most downloaded file is first
```

### US-009 — Accessible, responsive portal
**As** a portal user **I want** the portal usable on my laptop and tablet with a keyboard **so that**
I can work from anywhere.
**Priority** S · **Milestone** M3 · **Requirements** FR-GEN-003/004/007, NFR-016/017

```gherkin
Scenario: Keyboard-only upload
  Given I navigate with the keyboard only
  When I reach the upload control and activate it
  Then I can complete the flow without a mouse
  And focus is visible at every step

Scenario: Tablet layout
  Given a 768 px wide viewport
  Then navigation collapses to a menu
  And the file table remains usable without horizontal scrolling of actions

Scenario: Language selector
  Given the portal is open in Spanish
  When I switch the language to English
  Then navigation, labels, dates, and validation messages are in English
  And my preference is remembered for the next visit
```

---

## Epic E3 — Integrations and machine access

### US-010 — Register and manage an integration
**As** an administrator **I want** to register an integration with permissions and restrictions
**so that** enterprise systems get only the access they need.
**Priority** M · **Milestone** M1 · **Requirements** FR-M2M-001, 008, 009, 010, FR-ADM-001

```gherkin
Scenario: Create an integration
  Given I am an administrator
  When I create integration "crm-prod" with permissions "file.write,file.read"
  Then the integration exists with status "active"
  And I can issue an API key for it

Scenario: Disabled integration is rejected immediately
  Given integration "crm-prod" is disabled
  And it has a non-revoked API key
  When it calls GET /v1/integrations/files/{id}
  Then the response is 401 with code "invalid_api_key"

Scenario: IP allowlist enforcement
  Given the integration allowlist is "10.0.0.0/8"
  When it calls from 203.0.113.10
  Then the response is 403 with code "ip_not_allowed"
```

### US-011 — Authenticate with an API key and read metadata
**As** an enterprise system **I want** to call Files with an API key **so that** I can retrieve file
metadata without a user session.
**Priority** M · **Milestone** M5 · **Requirements** FR-M2M-002/003/007/008, FR-INT-001/003/005/006

```gherkin
Scenario: Valid API key retrieves metadata
  Given I hold an API key for integration "crm-prod" scoped to file.read
  When I call GET /v1/integrations/files/{fileId} with the key in the X-Api-Key header
  Then I receive metadata for a file I own
  And no S3 key, bucket, or credential is present in the response

Scenario: Unauthorized file is indistinguishable from missing
  Given the file belongs to another integration and no grant exists
  When I request its metadata
  Then the response is 404 with code "file_not_found"
  And no metadata is returned
```

### US-012 — Issue and revoke an integration API key
**As** an administrator **I want** to issue and revoke integration API keys **so that** I control
machine access without a shared secret.
**Priority** M · **Milestone** M1/M5 · **Requirements** FR-M2M-004/005/006/013, FR-ADM-002

```gherkin
Scenario: Issue a key (secret shown once)
  Given integration "crm-prod" exists with permission "file.read"
  When I create an API key with scopes "file.read"
  Then the response includes the full key exactly once
  And subsequent listing shows only the key's name and prefix, never the secret

Scenario: Revoke a key
  Given an API key is revoked
  When the consumer calls the integration API with it
  Then the response is 401 with code "invalid_api_key"
```

### US-013 — Rate limits and quotas
**As** a platform operator **I want** per-integration limits **so that** one consumer cannot degrade
service for others.
**Priority** S · **Milestone** M5 · **Requirements** FR-M2M-014, FR-INT-010, NFR-020

```gherkin
Scenario: Request rate limit
  Given my integration is limited to 60 requests/minute
  When I send 61 requests in a minute
  Then the 61st returns 429 with "Retry-After" and "RateLimit-*" headers

Scenario: Daily byte quota
  Given my integration has uploaded its daily quota
  When I request another upload session
  Then the response is 429 with code "quota_exceeded"
```

### US-014 — Delete an integration-owned file
**As** the owning integration **I want** to delete a file when authorized **so that** obsolete
content is removed.
**Priority** M · **Milestone** M5 · **Requirements** FR-INT-002, FR-DEL-007/009, FR-M2M-007

```gherkin
Scenario: Owner deletes its file
  Given I own the file and have file.delete
  When I call DELETE /v1/integrations/files/{id}
  Then the file immediately stops being downloadable
  And the object is physically deleted by the worker
  And an audit event records the integration as actor

Scenario: Deleting someone else's file is denied
  Given the file belongs to another integration
  When I call DELETE on it
  Then the response is 404
  And the file remains available
```

### US-015 — Upload through the integration API
**As** an enterprise system **I want** to upload files through the Files API in chunks **so that**
large files are stored in S3 without my application or the browser talking to the bucket.
**Priority** M · **Milestone** M2/M5 · **Requirements** FR-UP-002..007, 014..018, FR-INT-004/007

```gherkin
Scenario: Chunked upload of a 2 GiB file
  Given I have file.write and a 2 GiB file within my quota
  When I create an upload session with size, content type, and checksum
  Then I receive a file ID, upload session ID, chunk size, and chunk count
  And no S3 URL, bucket credential, or presigned token is present in the response
  And the file is not available yet
  When I upload all chunks (in any order) and complete the session
  Then Files verifies the staged file, uploads it to S3, and verifies the object
  And the file has indefinite retention, integration-only access, and no link

Scenario: Complete retry after an S3 failure
  Given all chunks are staged and the S3 commit failed transiently
  When I retry complete
  Then Files reuses the staged file and retries the S3 upload
  And I do not need to re-upload the chunks

Scenario: Size mismatch fails verification
  Given I declared 100 MiB but staged 90 MiB
  When I complete the session
  Then the response is 422 with code "upload_incomplete"
  And the file is not available

Scenario: Chunk access is session-scoped
  Given another integration owns a different upload session
  When I send a chunk to their session
  Then the response is 404
  And no bytes are written
```

### US-016 — Isolation of integration-owned files and explicit grants
**As** a security officer **I want** integration files isolated from humans and other integrations
**so that** sensitive customer documents are not exposed.
**Priority** M · **Milestone** M5 · **Requirements** FR-M2M-007/011/012, FR-FILE-005, FR-INT-005

```gherkin
Scenario: Integration file is invisible in portal search
  Given an integration owns "customer-123.pdf"
  When a portal user searches for "customer-123"
  Then the file does not appear

Scenario: Grant enables read-only cross-integration access
  Given an administrator grants integration B file.read on a file owned by integration A
  When B requests the content
  Then B receives the bytes and the access is audited
  When B attempts DELETE
  Then the response is 403

Scenario: Access attempt is audited
  Given integration B has no grant
  When B requests the file
  Then the response is 404
  And a denied download event is recorded with B's ID
```

### US-017 — Idempotent retries
**As** an enterprise system **I want** retries to be safe **so that** network failures do not create
duplicate files.
**Priority** M · **Milestone** M5 · **Requirements** FR-INT-007, NFR-006

```gherkin
Scenario: Same key and body returns the original response
  Given I created an upload session with Idempotency-Key "abc"
  When I retry the same request with the same body and key
  Then I receive the same file ID and session ID
  And no second file is created

Scenario: Same key with a different body is rejected
  Given Idempotency-Key "abc" was used for a 10 MiB PDF
  When I reuse "abc" for a 20 MiB PNG
  Then the response is 422 with code "idempotency_key_reuse"

Scenario: Concurrent duplicate requests
  Given two identical requests with the same key arrive simultaneously
  Then exactly one file is created
  And the second receives the same result (or 409 while in progress)
```

### US-018 — Check availability and download content
**As** an enterprise system **I want** a cheap availability check and an authorized content
download **so that** I can decide whether to fetch a file.
**Priority** M · **Milestone** M5 · **Requirements** FR-INT-002/008/009, FR-LINK-010/011

```gherkin
Scenario: Availability check does not transfer content
  Given I own an available file
  When I call GET /v1/integrations/files/{id}/availability
  Then I receive 200 with state "available", size, and expiry
  And no object bytes are transferred

Scenario: Expired file is unavailable
  Given the file expired
  When I check availability or download it
  Then the response is 404 with code "file_unavailable"
  And a denied download event is recorded
```

### US-019 — End-to-end integration with the existing product
**As** the existing product team **I want** a documented flow from customer record upload to
authorized download **so that** we can stop storing file contents locally.
**Priority** M · **Milestone** M5 · **Requirements** FR-INT-011, §15

```gherkin
Scenario: User uploads a file for a customer record
  Given a user is authorized for customer 4711 in the existing product
  When they upload a document
  Then the existing product backend creates an upload session as its integration
  And sends the file to Files in chunks through the API
  And stores the returned file ID with customer 4711
  And no file bytes are stored in the existing product database
  And the browser never receives an S3 URL or credential

Scenario: User downloads the customer document
  Given the user is still authorized for customer 4711
  When the existing product backend requests the file with its integration identity
  Then Files verifies the integration grant and streams the content
  And Files never sees the end user's credentials

Scenario: Browser cannot bypass business authorization
  Given a malicious user submits another customer's file ID to the existing product API
  When the existing product checks business authorization
  Then it denies the request before calling Files
  And even a forged call to Files fails the integration ownership check
```

---

## Epic E4 — Download links

### US-020 — Create a link with the admin-defined lifetime
**As** a file owner **I want** to create a public link **so that** I can share a file
without choosing an unsafe lifetime.
**Priority** M · **Milestone** M4 · **Requirements** FR-LINK-001..004, 015..017

```gherkin
Scenario: Public link uses the administrator's lifetime
  Given public links are enabled and the public TTL is 15 days
  When I create a public link with a maximum download count
  Then I receive a URL token and an 8-character key exactly once
  And the link expires in 15 days
  And there is no field for me to change the expiry
  And the key is not present in the URL and is stored only as an Argon2id verifier

Scenario: Public link disabled by policy
  Given the administrator disabled public links
  When I request a public link
  Then the response is 409 with code "public_links_disabled"
```

### US-021 — Redeem a public link
**As** a link visitor **I want** to enter the key and download the file **so that** I can retrieve
what was shared with me.
**Priority** M · **Milestone** M4 · **Requirements** FR-LINK-002/007..012, FR-ID-007, FR-GEN-002

```gherkin
Scenario: Correct key issues a short-lived grant and download succeeds
  Given an active public link with key "Ab3xY9Qz"
  When I submit the key
  Then I receive a grant token valid for 120 seconds
  And I download the file before it expires
  And the successful download count increments by one
  And the key is never echoed back

Scenario: Wrong key gets a generic failure
  Given an active public link
  When I submit a wrong key
  Then the response is 404 with code "link_not_found"
  And it is indistinguishable from an invalid token or revoked link
  And a failed attempt is recorded

Scenario: Grant is single-use and short-lived
  Given I used a grant once
  When I reuse it after completion or after 120 seconds
  Then the response is 404 with code "grant_not_found"

Scenario: Download limit is enforced under concurrency
  Given a link with max_downloads = 1
  When two grants are requested simultaneously
  Then only one reservation succeeds
  And the second receives 404 "link_not_found"
```

### US-022 — Revoke or rotate a link
**As** a file owner **I want** to revoke a link and create a new key **so that** I can stop access
when a key is leaked.
**Priority** M · **Milestone** M4 · **Requirements** FR-LINK-006/013

```gherkin
Scenario: Revocation is immediate
  Given I shared a public link
  When I revoke it
  Then any subsequent key attempt returns 404
  And existing unused grants for that link fail

Scenario: Rotation replaces the key
  Given I revoke the old link and create a new one
  Then the old key no longer works
  And the new 8-character key works
```

### US-023 — Abuse protection for key guessing
**As** a security officer **I want** failed key attempts throttled and monitored **so that** the
8-character key cannot be feasibly brute-forced.
**Priority** M · **Milestone** M4 · **Requirements** FR-LINK-009, §6

```gherkin
Scenario: Progressive delays and lockout
  Given 5 failed attempts for the same link from one IP within 15 minutes
  When a 6th attempt arrives
  Then it is rejected immediately with 429 and "Retry-After"
  And no key verification is performed
  And the lockout is recorded as a metric and audit event

Scenario: Distributed guessing is detected
  Given failures spread across IPs exceed the per-link threshold
  Then the link is soft-locked for 15 minutes
  And an alert is emitted to the operator dashboard
```

### US-024 — View download statistics
**As** a file owner **I want** to see how often my link was downloaded **so that** I know whether it
was useful or leaked.
**Priority** S · **Milestone** M4 · **Requirements** FR-LINK-014

```gherkin
Scenario: Stats reflect completed downloads only
  Given a link issued 10 grants, 7 completed, 2 failed, 1 expired
  When I view the link statistics
  Then successful downloads show 7
  And reserved/expired grants are reported separately
```

---

## Epic E5 — Retention, notifications, and deletion

### US-025 — Receive expiration notifications
**As** a file owner **I want** to be warned before my file expires **so that** I can extend retention
if I still need it.
**Priority** M · **Milestone** M6 · **Requirements** FR-NOT-001..010

```gherkin
Scenario: Intervals are sent once each
  Given intervals 7, 3, and 1 days and a file expiring in 7 days
  When the notification job runs
  Then exactly one 7-day email is queued and sent to my address
  And duplicate runs do not send a second email for that interval

Scenario: Retention extension reschedules
  Given the 3-day notification was sent
  When I extend the file by 30 days
  Then the 7/3/1-day notifications are recalculated for the new expiry
  And no notification from the old schedule is sent

Scenario: Indefinite files are suppressed
  Given an integration file has indefinite retention
  When the job runs
  Then no expiration notification is generated

Scenario: Acceptance vs delivery
  Given the email provider accepted the message
  Then the event shows "accepted"
  And only a confirmed delivery/bounce updates it to "delivered" or "bounced"
```

### US-026 — Automatic expiration and physical deletion
**As** a platform operator **I want** expired files to become unavailable and be physically removed
**so that** retention promises are kept.
**Priority** M · **Milestone** M6 · **Requirements** FR-DEL-001..006, FR-RET-008, FR-GEN-001

```gherkin
Scenario: Expiry blocks downloads before deletion
  Given a file expired at 00:00
  When a download is requested at 00:01
  Then the response is 404 "file_unavailable"
  And the file is marked expired/pending_deletion by the job
  And the S3 object is deleted by the worker

Scenario: Deletion is idempotent
  Given a pending_deletion file whose object is already gone
  When the deletion job runs again
  Then it records success (missing object = already deleted)
  And does not raise an error

Scenario: Recoverable S3 failure is retried
  Given S3 returns a 5xx or timeout for the delete
  When the job runs
  Then the file stays pending_deletion with an incremented attempt count and next retry time
  And the failure metric/alert fires
  And the file is never listed as available

Scenario: Expired file is not downloadable even with a valid link
  Given an active link to a file that just expired
  When the key is submitted
  Then the response is 404 "link_not_found"
```

### US-027 — Manually delete my file
**As** a file owner **I want** to delete a file **so that** it is no longer accessible.
**Priority** M · **Milestone** M3/M6 · **Requirements** FR-DEL-007/009, FR-AUD-001

```gherkin
Scenario: Manual deletion revokes access immediately
  Given my file has active links
  When I delete it
  Then all links stop working immediately
  And the file leaves my list (or shows as deleted)
  And the object is deleted asynchronously
  And an audit event records the action
```

### US-028 — Cleanup and reconciliation
**As** a platform operator **I want** automatic cleanup of incomplete uploads and reconciliation of
S3 and the database **so that** storage does not leak and states do not drift.
**Priority** M · **Milestone** M6 · **Requirements** FR-UP-008/013, FR-DEL-006

```gherkin
Scenario: Stale multipart upload is aborted
  Given an upload session older than 24 hours is incomplete
  When the cleanup job runs
  Then the S3 multipart upload is aborted
  And the file/session is marked failed/expired

Scenario: Orphan object is reported
  Given an S3 object exists with no file record
  When reconciliation runs
  Then it moves the object to the quarantine/cleanup prefix after the grace period
  And emits a reconciliation metric with the object key fingerprint (not full key)

Scenario: Missing object is quarantined in DB
  Given a file is marked available but its S3 object is missing
  When reconciliation runs
  Then the file is marked unavailable/quarantined
  And downloads are blocked
  And an alert is raised
```

---

## Epic E6 — Administration, audit, and operations

### US-029 — Audit trail
**As** an auditor **I want** to search immutable events **so that** I can investigate access and
changes.
**Priority** M · **Milestone** M1/M7 · **Requirements** FR-ADM-005/008, FR-AUD-001..003

```gherkin
Scenario: Access and changes are recorded
  Given a user uploads, renames, and downloads a file
  When I filter audit events by that file ID
  Then I see ordered events with actor, action, timestamp, and result
  And no file content or download key is present

Scenario: Auditor cannot download content
  Given I have the "auditor" role
  When I request file content or a download grant
  Then the response is 403

Scenario: Audit entries cannot be modified
  Given an audit record exists
  When any client attempts to update or delete it through the API
  Then the route does not exist (404/405)
```

### US-030 — Configure global policies
**As** an administrator **I want** to configure retention, notifications, quotas, and public-link
policy **so that** the platform matches company rules.
**Priority** M · **Milestone** M1/M6 · **Requirements** FR-RET-001..003/007, FR-NOT-001/003, FR-ADM-003/004/007, FR-DEL-010, FR-LINK-017

```gherkin
Scenario: New default applies only to new files
  Given the default retention changes from 30 to 60 days
  Then files created after the change expire in 60 days
  And existing files keep their current expiry

Scenario: Default retention cannot exceed 365 days
  Given the absolute maximum is 365 days
  When I set the default to 400 days
  Then the response is 422 with code "retention_exceeds_maximum"

Scenario: Legal hold blocks expiration and deletion
  Given an administrator places a legal hold on a file with a reason
  When the expiration job runs or an administrator requests deletion
  Then the file is not expired or deleted
  And every hold/release action is audited

Scenario: Link lifetimes are admin-defined
  Given the public TTL is 15 days and the private TTL is 30 days
  When a user creates links
  Then each link expires at its corresponding configured lifetime
  And the user cannot override it

Scenario: Template customization
  Given I edit the "expiring soon" template
  When a notification is next sent
  Then it uses the new subject and body
  And file content/credentials are never interpolated
```

### US-031 — Operate and observe the platform
**As** an operator **I want** health endpoints, metrics, dashboards, alerts, and restore runbooks
**so that** I can keep the SLOs and recover from failures.
**Priority** M · **Milestone** M0/M7 · **Requirements** FR-GEN-001/006, NFR-001..021

```gherkin
Scenario: Readiness reflects dependencies
  Given the database is reachable and migrations are current
  Then /health/ready returns 200
  When the database is unreachable
  Then /health/ready returns 503 and /health/live still returns 200

Scenario: Traces and metrics are emitted
  Given a download request
  Then a trace spans API and S3 calls with the request ID
  And RED metrics are recorded for the endpoint

Scenario: Deletion failures alert
  Given pending_deletion files exceed 5 for 15 minutes
  Then an alert routes to the on-call channel with the runbook link

Scenario: Single-host backup and restore
  Given the nightly `pg_dump` is in S3 under backups/postgres/
  When the instance is replaced and the dump is restored
  Then the database is recovered within the RPO/RTO target
  And the restore result is recorded in the drill log

Scenario: Host disk pressure
  Given the staging or PostgreSQL disk is above 85% utilization
  Then an alert fires before writes fail
  And the runbook covers cleanup and resize
```
