# Infrastructure, Environments, and IaC

**Decisions:** ADR-0010 · **Terraform:** [`infra/terraform/`](../../infra/terraform/) · **Costs:** [cost-model.md](cost-model.md)

## 1. Environments

| | dev | staging | prod |
|---|---|---|---|
| AWS account | `files-dev` | `files-staging` | `files-prod` |
| Region | us-east-1 | us-east-1 | us-east-1 (DR copies us-west-2) |
| VPC | 10.20.0.0/16 | 10.21.0.0/16 | 10.22.0.0/16 |
| AZs | 1–2 | 2 | 2 |
| NAT | 1 | 1 | 2 |
| RDS | db.t4g.small, single-AZ | db.t4g.medium, single-AZ | db.t4g.medium, Multi-AZ |
| RDS backups | 7 days | 14 days | 35 days + PITR |
| ECS API | 1 task, 0.5 vCPU/1 GiB | 2 tasks | 2–8 tasks, autoscale |
| ECS worker | 1 task | 1 task | 1 task (2 for HA optional) |
| S3 versioning | off | off | off (ADR-0008) |
| Public links | enabled | enabled | enabled (admin policy) |
| Malware scan | off | on | on (OQ-09) |
| Deletion protection | off | on | on |
| Domain | dev.files.* | staging.files.* | files.* |

Accounts isolate blast radius. Terraform state is per environment. No production data is copied
to lower environments.

## 2. Terraform layout

```
infra/terraform/
├── versions.tf            # Terraform/provider versions, commented backend
├── providers.tf           # AWS provider, default tags
├── variables.tf
├── locals.tf
├── main.tf                # module composition
├── outputs.tf
├── envs/dev.tfvars.example
├── envs/prod.tfvars.example
└── modules/
    ├── network/           # VPC, subnets, NAT, endpoints, SGs
    ├── storage/           # S3 bucket, BPA, SSE, lifecycle, CORS, policy
    ├── database/          # RDS PostgreSQL 18, subnet group, parameter group
    ├── compute/           # ECR, ECS cluster/services/tasks, ALB, IAM roles
    └── observability/     # CloudWatch alarms, dashboard, SNS
```

Remote state: S3 bucket `files-tfstate-<account>-<region>` with versioning + SSE, and S3 native
locking (`use_lockfile = true`) or DynamoDB locking. State buckets are bootstrapped manually
(documented) and never destroyed by CI. CI authenticates to AWS via GitHub OIDC
(`role-to-assume`), not long-lived access keys.

Commands:

```bash
cd infra/terraform
terraform init
terraform plan  -var-file=envs/dev.tfvars -var="db_password=$DB_PASSWORD"
terraform apply -var-file=envs/dev.tfvars -var="db_password=$DB_PASSWORD"
```

Prod applies require a reviewed plan artifact and a protected environment approval.

## 3. Network and security groups

- Internet → ALB (443/80) only. API tasks have no public IP.
- ALB → API tasks (8080) via `alb` SG; worker has no inbound.
- API/worker → RDS (5432) via `rds` SG.
- API/worker → internet:443 for Entra, Graph, S3 (or VPC endpoints), SigNoz, ECR/CloudWatch.
- S3 gateway endpoint for private access to buckets; optionally interface endpoints for ECR,
  CloudWatch Logs, Secrets Manager, STS to reduce NAT cost.
- Default SG: deny all. VPC flow logs enabled to CloudWatch (prod) with 14-day retention.
- No bastion in prod; troubleshooting via SSM Session Manager if absolutely required (no SSH keys).

## 4. IAM

| Role | Permissions |
|---|---|
| `files-<env>-api-task` | S3: `PutObject`, `GetObject`, `DeleteObject`, `AbortMultipartUpload`, `ListMultipartUploadParts`, `ListBucketMultipartUploads` on `f/*`, `staging/*`; `ListBucket` with prefix condition. No bucket-policy, no BPA, no `s3:*`. |
| `files-<env>-worker-task` | S3: `GetObject`, `DeleteObject` on `f/*`, `staging/*`; `PutObject`/`DeleteObject` on `orphan/*` (reconciliation); `ListBucket`, `AbortMultipartUpload`, `ListMultipartUploads`; scan read. |
| `files-<env>-execution` | ECR pull, CloudWatch Logs write, (optionally) Secrets Manager read. |
| CI deploy role | `ecr:PutImage`, `ecs:UpdateService/RegisterTaskDefinition`, `iam:PassRole` limited to task roles, `s3:PutObject` for artifacts, via GitHub OIDC. |
| Operator role | Read-only by default; break-glass for console actions, MFA, audited. |

No IAM user access keys in the runtime. The DB user is a dedicated application role (DML only);
Flyway uses a separate migration role with DDL privileges.

## 5. S3 configuration

- Bucket per environment, random suffix; account-region-unique.
- Block Public Access: all four flags; bucket owner enforced; ACLs disabled.
- Default encryption SSE-S3; every write sets encryption explicitly.
- Bucket policy: deny `s3:*` when `aws:SecureTransport = false`; deny non-TLS; no public principal.
- Lifecycle: abort incomplete multipart after 7 days; expire `staging/` after 1 day and
  `orphan/` after 7 days; (optional) noncurrent versions after 1 day.
- CORS: `PUT` from the configured portal origin, `Content-Type` header, 1 h max age.
- Event notifications are **not** used in v1 (scanning is polling-based); add S3 events later if
  scanning latency requires it.
- Access logging: S3 server access logs to a separate bucket or CloudTrail data events for
  `GetObject`/`DeleteObject` (cost trade-off; enable data events in prod for audit).

## 6. Secrets and configuration

- Non-secret configuration is injected as environment variables by the ECS task definition
  (`ASPNETCORE_*`, `S3__*`, `Entra__*`, `Cors__*`, `OTEL_*`), following the source's
  environment-first constraint.
- Sensitive values are injected as **ECS secrets** from AWS Secrets Manager: the database
  connection string is created automatically by Terraform and referenced as
  `ConnectionStrings__FilesDb`; additional secrets (Entra client secret, Graph secret, peppers)
  are passed through `app_secrets` as Secrets Manager ARNs. Local development uses `.env`
  values.
- Secret values: DB connection string, `Auth__ApiKeyPepper`, `Auth__LinkKeyPepper`, Graph client
  credentials, OTLP headers (if any). Non-secret config: tenant IDs, bucket, region, limits.
- `.env` files are gitignored; `.env.example` has placeholders. Secret scanning runs in CI.
- Rotation: DB password (RDS rotation; the secret value is ignored by Terraform after creation),
  API key pepper (dual-version verification), link key pepper (dual-version; old hashes retain
  their version prefix), Graph secret (Entra). See [runbooks.md](runbooks.md).
- Terraform state contains the DB password/connection string; the state bucket is encrypted and
  access-controlled. Automatic rotation requires the RDS-managed-secret pattern (documented
  follow-up).

## 7. Observability infrastructure

- CloudWatch Logs for containers (JSON), 30-day retention (prod), 7-day (dev/staging).
- **Access logging:** ALB/CloudFront access logs are disabled for token-bearing paths by default
  because URIs contain link/grant tokens. If the organization requires access logs, route them to a
  restricted bucket, retain ≤7 days, and treat them as secrets (see threat model I-11). WAF logging,
  if enabled later, must redact the URI.
- OpenTelemetry Collector sidecar or gateway (`otel/opentelemetry-collector-contrib`) exporting
  OTLP to SigNoz (self-hosted on ECS/EKS or external). Sampling: parent-based 10% at the
  collector for high-volume routes, 100% for jobs/errors (tail sampling if available).
- CloudWatch alarms (ALB 5xx/latency, ECS task count, RDS CPU/storage/connections) feed the SNS
  topic used by on-call; app-level alerts come from SigNoz.
- Dashboards: RED (API), uploads/downloads, link abuse, jobs/outbox, DB, S3, cost.

## 8. Backup and disaster recovery

| Asset | Backup | Retention | Restore |
|---|---|---|---|
| PostgreSQL | Automated backups + PITR; daily snapshot copy to us-west-2 | 35 days (prod) | Restore to a new instance, point app at it |
| S3 objects | Provider durability; optional cross-region replication | n/a | Objects remain unless deleted; no per-object restore (versioning off) |
| Terraform state | Versioned S3 bucket | 90 days | Re-apply |
| Config/secrets | Secret store versioning | per provider | Retrieve previous version |
| Container images | ECR, last 10 tags + release tags | 1 year (release tags) | Re-deploy |

- **RPO ≤15 min, RTO ≤4 h** (NFR-007). Quarterly restore drill into an isolated account/VPC.
- Deletion caveat: backups may retain deleted files up to 35 days (ADR-0008); the product says so.
- DR steps: declare incident → provision/restore DB in DR region (Terraform with a DR var) →
  restore snapshot/PITR → update DNS/ALB → validate smoke tests → reconcile S3 (single region,
  intact) → communicate. Detailed in [runbooks.md](runbooks.md).

## 9. Patching and maintenance

- Container base images rebuilt weekly (Dependabot/Renovate); critical CVEs patched within 48 h.
- RDS minor upgrades during maintenance windows (Sun 03:00–04:00 UTC); major upgrades planned with
  a restore test.
- ECS task definitions are immutable; deployments create new revisions.
- ACM certificates auto-renew; expiry alarm at 30 days.
- Quarterly access review of admin roles, integrations, and API keys.

## 10. Local development

`docker-compose.yml` provides PostgreSQL 18, LocalStack (S3-compatible), Mailpit (SMTP UI), and an
OpenTelemetry Collector. Flyway applies migrations via the `tools` profile. The API/worker run on
the host with `dotnet run` (or as compose services once images exist). See the root `README.md`.

This stack is for development only: LocalStack credentials are non-secret, Mailpit accepts any mail,
and the DB has a known password.
