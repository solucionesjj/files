# Open Questions, Assumptions, and Defaults

All product questions are resolved as of **2026-10-23**; the operational inputs were confirmed on
**2026-10-24** (see [§Operational inputs](#operational-inputs-confirmed-2026-10-24)). The table
records the answer now enforced by the design. If a decision changes, update `decisions.md`, the
affected requirement, ADR, schema, and tests in the same change.

| ID | Question | Resolution | Impact | Owner |
|---|---|---|---|---|
| OQ-01 | Sending mailbox and mechanism; POP cannot send. | Microsoft 365 mailbox **`notificationsagent@ecs-la.com`**; POP for receiving, **SMTP AUTH** `smtp.office365.com:587` (STARTTLS) for sending. Password/app password is configured in `/opt/files/.env`; SMTP AUTH must be enabled. Graph `Mail.Send` is the fallback. | Notifications (M6). | Platform/Identity |
| OQ-02 | Cloud, region, network. | AWS **`us-east-1`**, account **default VPC/public subnet**, one EC2 + one S3 bucket. | IaC, latency, cost. | Architecture |
| OQ-03 | One or multiple Entra tenants? | **One tenant per deployment.** | No `tenant_id` on every table; isolation by deployment. | Product |
| OQ-04 | Who can redeem a private link? | Owner selects **specific Entra users/groups** or enables **any authenticated tenant user**; the API enforces the ACL. | Link ACL table, directory search, Graph membership checks. | Product/Security |
| OQ-05 | May admins download content? | **No.** Admins manage metadata, retention, legal hold, and deletion only. | No break-glass content path in v1. | Security/Legal |
| OQ-06 | Storage/ingress/concurrency quotas. | **5 GiB/file, 500 GiB total, 200 GiB/integration, 50 GiB/day, 3 concurrent uploads.** Admin-configurable. | EBS sizing, throttling, alerts. | Operations |
| OQ-07 | Human retention policy. | **Default 30 days**, admin-configurable for new files; **absolute maximum 365 days**, no admin exceptions; user may extend up to 365 with a **mandatory note**; indefinite forbidden for humans. Every change audited. | Lifecycle, UI, schema, audit. | Product/Legal |
| OQ-08 | Legal/litigation hold. | **In v1.** Admin sets/releases with a reason; blocks expiration and deletion; audited. | Admin workflow, jobs, schema. | Legal |
| OQ-09 | Malware scanning. | **ClamAV mandatory in production**, scanning staged files **before** S3; infected files never uploaded; `t3.large`. | Security, latency, memory. | Security |
| OQ-10 | Audit/download retention. | **Audit 60 months; download events 24 months**; notification events 12 months. | Storage, purge job. | Security/Legal |
| OQ-11 | API keys vs Entra. | **Entra client credentials only**; no API keys are issued or stored. | Removes credential table/endpoints/rotation. | Integrations |
| OQ-12 | Folders/hierarchy. | **No folders**; flat list + tags. | Model/UI unchanged. | Product |
| OQ-13 | Expected volume. | **200 files/day, p95 50 MiB, 20 concurrent downloads, 3 concurrent uploads.** | Capacity/cost/SLO. | Integrations |
| OQ-14 | UI language. | **Spanish and English with a selector**; Spanish default; emails per locale. | Angular i18n, templates. | Product |
| OQ-15 | Domain/TLS. | **`files.sacservices.cloud`** at Network Solutions, A record → host public IP, Caddy/Let's Encrypt (no ACM). | Caddy, links, Entra redirect URIs. | Infra |
| OQ-16 | Key lockout policy. | **5 failures/link+IP/15 min; 15-min lockout; 100 failures/link/hour.** | Abuse protection. | Security |
| OQ-17 | DR model. | **Restore-based**: replacement EC2 + latest `pg_dump`; RPO 24 h, RTO 4 h; no standby. | Cost/runbooks. | Business |
| OQ-18 | Admin delete of integration files. | **Yes**, with mandatory reason and audit. | Admin API/UI. | Product |
| OQ-19 | In-browser preview. | **No**, download only. | Scope. | Product |
| OQ-20 | Budget target. | **~US$110–125/month** production (`t3.large`, ClamAV, 100 GiB EBS, 500 GiB S3, 200 GiB egress); dev without scanning ~US$26/month. | Instance/storage/cost. | Finance |
| OQ-21 | Link lifetimes. | **Public 15 days, private 30 days** by default; both admin-configurable for new links; owner **cannot** change them. Existing links keep their expiry. | Link creation, schema, UI. | Product |
| OQ-22 | SDK. | **OpenAPI 3.1 + cURL/.NET examples**; no versioned SDK. | Integration effort. | Integrations |
| OQ-23 | Upload resume. | Resume within the **60-minute session**; no cross-session resume. | Staging cleanup/UX. | Product |
| OQ-24 | Download notifications. | **No emails**; owner sees statistics. | Scope. | Product |
| OQ-25 | Exit single-node phase. | Any of: **CPU/disk >80% sustained, need for >1 API/HA, RPO <24 h**. | Scale-up planning. | Architecture/Finance |
| OQ-26 | Bare public IP acceptable? | **No** for production; use `files.sacservices.cloud`. | TLS trust. | Product/Infra |

## Assumptions

1. One Entra tenant and one AWS account per environment.
2. The existing product can call HTTPS and send 16 MiB chunks; it never needs S3 access.
3. Files are opaque blobs; no preview/conversion (OQ-19).
4. One EC2 + one S3 bucket; no HA, PITR, or other AWS services in this phase.
5. The existing product owns business authorization; Files enforces file-level access only.
6. Users have an Entra email (`mail` or `preferred_username`); missing email fails notifications.
7. The team can run Docker locally with .NET 10 and Node 20+.
8. Public-link visitors use a modern browser and can copy/paste the 8-character key.
9. Production authenticates to S3 with a static access key in `/opt/files/.env`; the existing bucket
   policy restricts access to the host.
10. TLS terminates at Caddy; the API is only on the internal Docker network.
11. Spanish is the default UI language; English is selectable; code/docs are English.
12. No API keys: every machine consumer is an Entra application.

## Resolved answers applied (2026-10-23)

- Retention: **30/365 days**, note required above default, no exceptions, legal hold enabled.
- Links: **public 15 days / private 30 days**, server-defined; private ACL by users/groups or
  all-tenant.
- Access: admins **cannot** read content; one tenant; no folders, no preview, no download emails.
- Machines: **Entra client credentials only**.
- Audit: **60 months** for audit, **24 months** for downloads.
- UI: **Spanish + English**.
- Operations: restore-based DR; single-node exit on capacity/HA/RPO triggers.
- Email: `notificationsagent@ecs-la.com` via SMTP AUTH; POP is inbound only.

## Operational inputs confirmed (2026-10-24)

The owner answered the deployment questions. These are operational inputs, not product decisions;
they unblock S13–S14.

| Topic | Confirmation | Still needed |
|---|---|---|
| AWS / S3 | Existing host (Ubuntu 24.04, public IP, SSH) and an **existing S3 bucket**, both managed outside this repo; **no Terraform**. The host authenticates to S3 with a **static access key**. `us-east-1`. | Bucket name + `S3__AccessKeyId`/`S3__SecretAccessKey`; the bucket policy already restricts access to the host (not modified by Files). |
| Host access | Administration over **SSH via the public IP**; the firewall/security group is already configured and left untouched. | Nothing (SSH key delivered by the infra team). |
| Entra ID | Tenant and app registrations already exist. | `Entra__TenantId`, `Entra__ApiClientId`, `Entra__PortalClientId`. |
| Graph ACL | `User.ReadBasic.All`, `Group.ReadBasic.All`, `GroupMember.Read.All` will be granted with admin consent. | Confirm consent before M4. |
| Email | SMTP AUTH is enabled on `notificationsagent@ecs-la.com`; the app password is delivered out of band. | App password in `/opt/files/.env`; verify a send in M6. |
| DNS/TLS | Infra team creates the `A` record for `files.sacservices.cloud` once the host's public IP is known. | Confirmation that the record exists. |
| Environments | **Production only**; development/test run 100 % locally (LocalStack). Gate 9 relaxed to a rehearsal on a disposable instance/local PostgreSQL 18. | Run and record that rehearsal before S14. |
| Budget | ~US$110–125/month approved. | Enable AWS budget alerts. |
| Deployment | Manual, per `deploy/README.md`. | — |
| Tenant model | One Entra tenant per deployment confirmed (D-01 now Accepted). | — |

**Resolved (2026-10-25):** launch gate 9 no longer requires a standing staging environment. The
exact production Flyway sequence and the rollback are rehearsed on a disposable instance or local
PostgreSQL 18, and the result is recorded. A permanent staging environment remains out of scope.

## How to change an answer

1. Add the new decision to `decisions.md` and the relevant ADR.
2. Update requirements/stories/tests, schema, and OpenAPI in the same change.
3. Update this file and the roadmap if a milestone gate changes.
