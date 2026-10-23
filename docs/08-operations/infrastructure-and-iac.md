# Infrastructure, Environments, and IaC

**Decisions:** ADR-0010, ADR-0003, D-30…D-33 · **Terraform:** [`infra/terraform/`](../../infra/terraform/) ·
**Costs:** [cost-model.md](cost-model.md)

## 1. Initial topology

One EC2 instance and one S3 bucket per environment. No ALB, ECS, RDS, ECR, NAT gateway, or
CloudWatch.

| Resource | Value |
|---|---|
| AMI | Ubuntu 24.04 LTS x86_64 (Canonical) |
| Instance | `t3.large` (2 vCPU, 8 GiB) default for ClamAV + PostgreSQL + API/worker |
| Root volume | 100 GiB gp3, encrypted, `delete_on_termination = false` |
| Address | Elastic IP (stable public IPv4) |
| Network | Default VPC public subnet, or an existing `vpc_id`/public `subnet_id` |
| Security group | 80/443 from `0.0.0.0/0`; 22 only from `admin_cidr`; all egress |
| S3 | One private bucket, SSE-S3, BPA, versioning off |
| IAM | One EC2 instance role, prefix-scoped S3; bucket policy denies all other principals |
| Host software | Docker Engine + Compose plugin, AWS CLI, UFW, fail2ban, ClamAV container |
| DNS/TLS | A record → Elastic IP; Caddy automatic HTTPS (domain required) |

Environments: start with one (`prod` or `staging`). Local development uses Docker Compose on the
developer machine. A separate staging EC2 can be created later with a different state file and
`environment` value; do not share the production bucket.

## 2. Terraform layout

```
infra/terraform/
├── versions.tf            # Terraform/provider pins, commented S3 backend
├── providers.tf           # AWS provider, default tags
├── variables.tf
├── locals.tf
├── main.tf                # instance + storage modules, IAM policy
├── outputs.tf
├── terraform.tfvars.example
└── modules/
    ├── instance/          # EC2, EIP, SG, IAM role/profile, user_data
    └── storage/           # S3 bucket, BPA, SSE, lifecycle, restrictive policy
```

Remote state: an S3 bucket with versioning and SSE (`use_lockfile = true`) in the same account,
bootstrapped once. State contains no application secrets (the DB password is provisioned in
`/opt/files/.env` by the deployment pipeline, not Terraform). Never commit `.tfstate` or real
`.tfvars`.

Commands:

```bash
cd infra/terraform
terraform init
export TF_VAR_admin_cidr="203.0.113.10/32"
export TF_VAR_ssh_key_name="files-admin"
terraform plan  -var-file=envs/prod.tfvars -out=prod.tfplan
terraform apply prod.tfplan
```

The apply outputs the Elastic IP, instance ID, bucket name, and an SSH command.

## 3. Host bootstrap (`user_data`)

- Installs Docker Engine + Compose plugin, AWS CLI, UFW, fail2ban, unattended-upgrades, chrony.
- Creates `/opt/files` (deploy/config) and `/var/lib/files/{staging,postgres,portal}`; 2 GiB swap.
- Enables Docker, UFW (22/80/443), fail2ban, unattended upgrades.
- Does **not** clone the application or write secrets. Deployment copies `deploy/` and writes
  `/opt/files/.env` (mode 0600) from GitHub Environment secrets.

First deployment steps (documented in `deploy/README.md`):

1. Point DNS `files.sacservices.cloud` (Network Solutions) at the Elastic IP.
2. Copy `deploy/` to `/opt/files/deploy`.
3. Create `/opt/files/.env` with DB password, Entra IDs/secrets, peppers, M365 SMTP mailbox, S3 bucket.
4. Start PostgreSQL, run migrations, build/start API/worker/Caddy.
5. Install the nightly backup cron and verify one dump in S3.

## 4. Networking and exposure

- Internet → EC2 security group 80/443 → Caddy. SSH 22 only from `admin_cidr`.
- API `:8080` is not published; Caddy reaches it over the Docker network.
- PostgreSQL has no host port; it is reachable only by API/worker containers.
- Host egress 443 to S3, Entra, ClamAV signatures, Docker Hub/GitHub, and SigNoz; **587 to
  smtp.office365.com** for email. No inbound database access.
- UFW mirrors the security group as defense in depth; fail2ban watches SSH.
- IMDSv2 required; the AWS SDK uses IMDSv2 through the host network.

## 5. S3 access model (exclusive)

- Bucket policy **denies** `s3:GetObject`, `s3:PutObject`, `s3:DeleteObject`, `s3:ListBucket`,
  `s3:AbortMultipartUpload`, `s3:ListMultipartUploadParts`, and
  `s3:ListBucketMultipartUploads` to every principal whose ARN is not the EC2 instance role.
- The instance role policy allows only `f/*`, `staging/*`, `orphan/*`, and `backups/*` (plus
  prefix-scoped bucket listing). No static access keys exist.
- Infrastructure automation can manage bucket configuration (policy, lifecycle, encryption) but
  cannot read/write objects; this is the only documented exception to “only Files touches data”.
- No presigned URLs and no S3 CORS; the browser never talks to S3. The API CORS allowlist is
  used only for local development.
- Lifecycle: abort incomplete multipart after 7 days; expire `staging/` after 1 day, `orphan/`
  after 7 days, `backups/postgres/` after 35 days; versioning disabled (ADR-0008).

## 6. Secrets and configuration

- `/opt/files/.env` (0600, owned by the deploy user) contains:
  `POSTGRES_PASSWORD`, `ConnectionStrings__FilesDb`, `Entra__*`, `Auth__ApiKeyPepper`,
  `Auth__LinkKeyPepper`, `Notifications__*`, `Storage__StagingPath`, `S3__Bucket`, `S3__Region`,
  `OTEL_*`, feature flags.
- The API/worker use the EC2 instance role for S3; **no** `S3__AccessKeyId`/`SecretAccessKey` in
  production.
- Secrets are provisioned by GitHub Actions from Environment secrets over SSH; they are never in
  the repository, Terraform state, Docker images, or logs.
- Local development uses `.env` with LocalStack credentials; those are non-secret.
- Rotation: DB password (update `.env` + `ALTER ROLE`, restart API), peppers (versioned columns),
  Graph/Entra secrets (Entra + `.env` + restart), SSH keys. See [runbooks.md](runbooks.md).

## 7. TLS and DNS

- A public IPv4 alone cannot obtain a publicly trusted certificate. A DNS name is required for
  production use (OQ-15).
- Route 53 is **not** required; the domain may be managed anywhere. Create an `A` record to the
  Elastic IP.
- Caddy obtains/renews the certificate automatically (HTTP-01/TLS-ALPN) once ports 80/443 reach
  the instance. It also redirects HTTP to HTTPS and sets HSTS.
- For a temporary test without a domain, use `sslip.io`/`nip.io` pointing at the Elastic IP, or
  a self-signed cert restricted to test IPs. Do not expose self-signed TLS as production.
- Caddy access logs are disabled (token-bearing URIs); see threat model I-11.

## 8. Backups and disaster recovery

| Asset | Backup | Retention | Restore |
|---|---|---|---|
| PostgreSQL | Nightly `pg_dump -Fc` gzip to `s3://<bucket>/backups/postgres/` via host cron | 35 days | Download, `pg_restore` into a fresh container/instance |
| S3 objects | Provider durability; versioning disabled | n/a | Objects remain unless deleted |
| EBS | Optional manual snapshot | per policy | Attach to a new instance |
| Terraform state | Versioned S3 backend | 90 days | Re-apply |
| Config | `.env` backed up in the team secret store | n/a | Re-provision |
| Images | Rebuild from Git tag | Git history | Rebuild/restart |

- Initial **RPO ≤24 h** and **RTO ≤4 h** (D-31). PITR/WAL archiving is not available until the DB
  moves to a managed service. S3 has versioning/replication disabled, so object loss is a
  documented residual risk; enable one of them before the data becomes critical.
- The backup job records success/failure in the journal and a `backups/postgres/_latest` marker;
  a worker job verifies the marker exists and is <26 h old, otherwise alerts.
- Quarterly restore drill into a fresh instance/VPC; record the result in
  [runbooks.md](runbooks.md#drill-log).

## 9. Observability infrastructure

- ClamAV (`clamd` 3310) scans staged files before S3; signatures on a 2 GiB volume; one scan at
  a time. The published image is amd64-only (the production EC2 is x86_64); local Apple Silicon
  development keeps scanning off.
- OTel Collector container (optional profile) exports OTLP to SigNoz (self-hosted on the same EC2
  if resources allow, or external). On `t3.large`, self-hosting SigNoz may be tight alongside ClamAV; an external
  SigNoz instance is preferred.
- Host metrics: `docker stats`, `node_exporter` if SigNoz is added, `df`, `free`, `systemd` logs.
- Logs: Docker `json-file` with rotation (e.g., 10 MB × 3) or journald; no access logs for
  token-bearing paths.
- Uptime: an external HTTPS check against `/health/ready` (and the SPA) is recommended; no
  CloudWatch alarms in this phase.
- Alerts (SigNoz or external): host down, disk >80/90%, container restart loop, 5xx rate,
  deletion/outbox backlog, backup missing.

## 10. Patching and maintenance

- Ubuntu security updates via unattended-upgrades; reboot window monthly (or when the kernel
  requires it). Docker images rebuilt weekly; critical CVEs within 48 h.
- Maintenance requires a short API/worker restart. Caddy serves a maintenance page or returns
  502 during the gap; announce the window.
- PostgreSQL minor upgrades: stop API/worker, backup, upgrade container image, start, verify.
- Keep the deploy user and SSH keys minimal; use `sudo` only for Docker/system actions.

## 11. Local development

`docker-compose.yml` provides PostgreSQL 18, LocalStack S3, Mailpit, and an OTel Collector.
Flyway applies migrations via the `tools` profile. The API/worker run on the host with
`dotnet run`. See the root `README.md`.

This stack is for development only: LocalStack credentials are non-secret, Mailpit accepts any
mail, and the DB has a known password. The SPA runs via `ng serve` at `localhost:4200`; no S3 CORS
is required.

## 12. Scale-up path

1. Add a second EC2 and a load balancer (or move to ECS/ALB).
2. Move PostgreSQL to RDS for PITR/Multi-AZ.
3. Move staging to shared/durable storage if multiple API instances write uploads (or upload
   directly from each API instance to S3 multipart with session affinity).
4. Add CloudWatch/WAF/Route 53 or keep Caddy/DNS, plus centralized logs/SigNoz.
