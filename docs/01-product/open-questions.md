# Open Questions, Assumptions, and Defaults

Items marked **Blocking** must be answered before the affected milestone exits. Non-blocking
items have a documented default that is used unless overridden. When an answer changes a
default, update `decisions.md`, the requirement, and the affected tests in the same change.

| ID | Question | Default used now | Impact | Blocking? | Owner |
|---|---|---|---|---|---|
| OQ-01 | The spec says "Email: MS365 Pop account", but POP3 cannot send mail. Which sending mechanism is approved: Microsoft Graph `sendMail`, SMTP AUTH with OAuth2, or a relay (e.g., SendGrid/SES)? Which mailbox/address should send? | Microsoft Graph `sendMail` from a dedicated shared mailbox, application permission `Mail.Send` restricted with an application access policy to that mailbox. | Notification milestone (M6) and production readiness. Graph requires Entra app permission and mailbox provisioning. | **Yes — M6** | Platform/Identity team |
| OQ-02 | Is AWS the approved cloud and region? Are there data-residency requirements (Colombia)? | AWS, `us-east-1`, single region with backups in `us-west-2`. | IaC, latency, egress cost, compliance. | No (default) | Architecture |
| OQ-03 | Will Files ever serve multiple Entra tenants in one deployment? | No — one tenant per deployment (D-01). | Adds `tenant_id` everywhere and per-tenant policies. | No | Product |
| OQ-04 | Should any authenticated tenant user with a private link be able to download, or must the creator/owner also grant specific users? | Any authenticated user in the configured tenant with a valid active private link. This is the §5.3 default. | Authorization model and link UI. | No | Product/Security |
| OQ-05 | May administrators download user file content for support? | No by default. Admin can view metadata, change retention, and delete with reason. | Privacy/compliance; break-glass feature if yes. | No | Security/Legal |
| OQ-06 | What are the production quotas: total storage, per-integration storage, daily ingress, max file size, concurrent uploads? | 500 GiB total, 200 GiB/integration, 50 GiB/day ingress, 5 GiB max file, 5 concurrent uploads/integration. | Cost model and throttling. | No (soft launch) | Operations |
| OQ-07 | Confirm retention defaults: 365 days default for humans, 3650 max, user override allowed, indefinite forbidden for humans. | As stated (D-08). | Data lifecycle and user expectations. | No | Product/Legal |
| OQ-08 | Is a legal/litigation hold required (block deletion even for expired files)? | No in v1; admin override/disable-deletion flag is prepared as a column (`legal_hold`) for future use. | Compliance; adds admin workflow. | No | Legal |
| OQ-09 | Is malware scanning mandatory at launch? Which engine and signatures SLA? | Optional in v1; ClamAV in production, `not_scanned` accepted in dev. | Security posture, latency (scan before `available`), infra cost. | No (recommended yes) | Security |
| OQ-10 | Audit-log retention: 24 months? Download events 12 months? | 24 / 12 months, configurable. | Storage, compliance. | No | Security/Legal |
| OQ-11 | Must API keys be generally available at launch, or is Entra client credentials sufficient for the first consumer? | Both implemented; Entra primary, API keys available but disabled per integration unless requested. | M2M milestone scope, secret-handling procedures. | No | Integrations |
| OQ-12 | Are folders/hierarchies needed for portal organization? | No — flat list + tags. | Data model and UI; would add `folders` and path rules. | No | Product |
| OQ-13 | What are the existing product's expected volumes: files/day, average and p95 file size, peak concurrent downloads? | 200 files/day, p95 50 MiB, peak 20 concurrent downloads. | Capacity, cost, SLOs, DB sizing. | No (assumed) | Integrations |
| OQ-14 | UI language: Spanish only, English only, or bilingual? | Spanish UI initially (users in Colombia), English code/docs. | i18n effort and templates. | No | Product |
| OQ-15 | Production DNS/branding: domain, TLS certificate ownership, email sender display name? | `files.<company>.com`, ACM certificate, "Files" sender. | ALB/ACM IaC and public link URLs. | No (needed for prod cutover) | Infra |
| OQ-16 | Public key lockout policy: how many failed attempts before lockout, for how long? | 5 failures per link+IP per 15 min; progressive delay 0.5 s, 1 s, 2 s, 4 s; 15 min lockout; global per-link limit 100 failures/hour. | Security vs. usability; see threat model. | No | Security |
| OQ-17 | Is a warm-standby DR region required, or is restore-from-backup acceptable? | Restore-based DR; RTO 4 h, RPO 15 min; backups copied to `us-west-2`. | Cost and architecture. | No | Business |
| OQ-18 | May administrators delete integration-owned files? | Yes, with reason and audit; integration is notified only through audit/API, no email. | Data ownership disputes. | No | Product |
| OQ-19 | Is in-browser preview required for PDFs/images? | No in v1 (out of scope). | Adds AV/rendering/security scope. | No | Product |
| OQ-20 | What is the monthly infrastructure budget target? | ~US$360–400/month production baseline (see cost model; $180–320 requires a single NAT, single-AZ DB, or no self-hosted SigNoz). | Sizing and instance choices. | No | Finance |
| OQ-21 | Should public links expire by default? | No default expiry, but optional and an admin-configurable maximum lifetime (default 90 days) is shown in the UI. | Security vs. sharing convenience. | No | Product |
| OQ-22 | Does the existing product need a downloadable SDK or just OpenAPI + examples? | OpenAPI 3.1 + cURL/.NET examples; no SDK in v1. | Integration effort. | No | Integrations |
| OQ-23 | Are uploads allowed from browsers with unstable connections? Resume across sessions? | Resume within the 60-min session using S3 multipart part listing on the client; no cross-session resume in v1. | UX and upload UX complexity. | No | Product |
| OQ-24 | Do we need to notify users when a link is downloaded or revoked? | No notification in v1; visible in portal stats. | Scope. | No | Product |

## Assumptions

1. The deployment serves a single Entra tenant and a single AWS account per environment.
2. The existing enterprise product can reach the Files API over HTTPS and can reach S3
   presigned URLs (or use the staging upload path if egress to S3 is restricted).
3. Files are opaque binary blobs; no transformation, conversion, or preview is required.
4. The existing product is the source of business authorization; Files enforces only file-level
   authorization and integration boundaries.
5. Users have an email address in Entra (`mail` or `preferred_username`); notifications fail
   permanently if no email is available.
6. The team can run Docker locally and has .NET 10 and Node 20+ installed.
7. Public link visitors use a modern browser and can copy/paste the 8-character key.
8. AWS IAM roles (not long-lived access keys) are used by the runtime in production.
9. TLS terminates at the ALB; the API only accepts traffic from the ALB/private network.
10. Spanish is the primary UI language; technical documentation and code are in English.

## How to resolve

1. Add the answer to this file (keep the ID), mark it resolved with date and owner.
2. Update `decisions.md` and any ADR.
3. Update affected requirements/stories/tests; note it in the PR description.
4. For blocking items, update the milestone exit criteria in `docs/09-delivery/roadmap.md`.
