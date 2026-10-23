# Files — AWS Terraform skeleton

Skeleton-level Terraform for deploying **Files** to AWS. It models the production
shape (VPC, private S3 storage, PostgreSQL, ECS Fargate, ALB, alarms) without
attempting to be a complete platform. One environment is managed per root
invocation; dev, staging, and prod must use separate state.

## Layout

```
infra/terraform/
├── versions.tf                 # Terraform/provider pins, commented S3 backend
├── providers.tf                # AWS provider + common default tags
├── variables.tf                # Root inputs and validations
├── locals.tf                   # files-<env> prefix and common tags
├── main.tf                     # Module wiring
├── outputs.tf                  # Bucket, DB, ECS/ECR, ALB outputs
├── terraform.tfvars.example
├── envs/
│   ├── dev.tfvars.example
│   └── prod.tfvars.example
└── modules/
    ├── network/                # VPC, 2 AZs, public/private subnets, NAT, S3 endpoint
    ├── security/               # ALB, API, worker, and RDS security groups/rules
    ├── storage/                # S3 bucket, policies, lifecycle, CORS
    ├── database/               # RDS PostgreSQL 18, subnet/parameter groups
    ├── compute/                # ECR, IAM, logs, ECS, ALB, autoscaling
    └── observability/          # SNS + CloudWatch alarms
```

The security groups live in their own module so `database` and `compute` both
depend on `security` (and `network`) without depending on each other.

## What is modeled

- VPC `10.20.0.0/16` across two AZs, with two public and two private `/24`
  subnets, an internet gateway, one NAT gateway by default (two for prod), route
  tables, and an S3 gateway endpoint.
- S3 bucket named
  `files-<env>-<account-id>-<region>-<random-suffix>`, with all four Block
  Public Access flags, owner-enforced ownership, SSE-S3, optional versioning,
  TLS-only bucket policy, lifecycle cleanup, and direct-upload CORS.
- RDS PostgreSQL 18 (`18.6`), private subnets, storage autoscaling,
  environment-based backups (7/14/35 days for dev/staging/prod), final snapshot,
  encrypted storage, `rds.force_ssl=1`, and UTC.
- ECR repositories for API and worker with scan-on-push, immutable tags, and a
  retain-10-images lifecycle policy.
- ECS Fargate cluster with Container Insights, separate API/worker task
  definitions and services, private networking, scoped IAM roles, 30-day logs,
  API target-tracking autoscaling, ASP.NET Core configuration environment
  variables, and Secrets Manager injection for the database connection string
  and any additional application secrets.
- Internet-facing ALB in public subnets. Without an ACM certificate it exposes
  only HTTP (dev convenience); with a certificate it adds HTTPS and redirects
  HTTP to HTTPS.
- CloudWatch alarms for ALB 5xx rate, ALB p95 response time, RDS CPU/free
  storage, and API running-task count, wired to SNS with an optional email
  subscription.

## Prerequisites

- Terraform **>= 1.9** (validated here with Terraform 1.13.4).
- AWS credentials with permissions for the resources above.
- An ACM certificate in the target region **before** enabling HTTPS. The
  certificate request/validation itself is outside this stack.
- An S3 bucket (and lock mechanism) for remote state, bootstrapped separately.
- No Docker is required for `terraform init`/`validate`.

Provider versions are pinned in `versions.tf` (`aws ~> 6.0`, `random ~> 3.6`)
and frozen in `.terraform.lock.hcl`.

## Remote state bootstrap

No backend is configured by default, so `terraform init -backend=false` works for
validation. For real use:

1. Create a versioned, SSE-encrypted, public-access-blocked S3 bucket for state.
   Enable access logging if required by your organization.
2. Add a lock mechanism:
   - Terraform 1.10+ can use S3 native locking with `use_lockfile = true`.
   - Otherwise create a DynamoDB table with a `LockID` string partition key and
     set `dynamodb_table` in the backend block.
3. Give each environment its own state key, for example
   `files/dev/terraform.tfstate`, `files/staging/terraform.tfstate`, and
   `files/prod/terraform.tfstate`.
4. Uncomment and edit the `backend "s3"` example in
   [versions.tf](versions.tf), then run `terraform init`.

Never commit `.tfstate`, plan files, or real `.tfvars` files.

## Variables

Set the RDS password out of band; it is deliberately absent from all example
files:

```bash
export TF_VAR_db_password='replace-with-a-strong-16+-character-password'
```

| Variable | Default | Notes |
|---|---|---|
| `region` | `us-east-1` | AWS region for the whole stack. |
| `environment` | `dev` | `dev`, `staging`, or `prod`; used in `files-<env>` names. |
| `vpc_cidr` | `10.20.0.0/16` | Must support two public and two private `/24`s. |
| `nat_gateway_count` | `1` | Set `2` for prod multi-AZ egress. |
| `portal_origin` | `http://localhost:4200` | Exact CORS origin; no path or trailing slash. |
| `bucket_versioning` | `false` | Enables versioning and 1-day noncurrent expiration. |
| `db_engine_version` | `18.6` | Pinned PostgreSQL 18.x; see the note below. |
| `db_instance_class` | `db.t4g.medium` | RDS instance class. |
| `db_password` | none — required | Sensitive, minimum 16 characters. Stored in Secrets Manager and injected into ECS. |
| `app_secrets` | `{}` | Map of ASP.NET config key to Secrets Manager ARN, injected as ECS secrets (for example `Entra__ClientSecret`). |
| `app_environment` | `{}` | Extra non-secret ASP.NET config variables (Entra IDs, CORS origins, portal URL). |
| `otel_endpoint` | `""` | OTLP endpoint for the API/worker. |
| `multi_az` | `false` | Recommended `true` for prod. |
| `deletion_protection` | `false` | Recommended `true` for prod. |
| `certificate_arn` | `""` | ACM cert ARN. **Required for prod**; empty means HTTP only. |
| `api_image`, `worker_image` | `""` | Empty resolves to the generated ECR repo at `:bootstrap`. |
| `api_cpu` / `api_memory` | `512` / `1024` | Fargate API task size. |
| `worker_cpu` / `worker_memory` | `512` / `1024` | Fargate worker task size. |
| `api_desired_count` | `2` | Baseline API count. |
| `worker_desired_count` | `1` | Worker count. |
| `api_autoscaling_min` / `max` | `2` / `8` | API target-tracking bounds. |
| `api_cpu_target` | `60` | Target average API CPU percentage. |
| `log_retention_days` | `30` | CloudWatch Logs retention. |
| `alarm_topic_arn` | `""` | Empty creates an SNS topic in this stack. |
| `alarm_email` | `""` | Optional email subscription when this stack creates the topic. |

`db_engine_version` defaults to **18.6**, a published RDS for PostgreSQL 18.x
minor release. Terraform accepts the provider value; the RDS API remains the
final authority on availability in a region. If a region is missing 18.6,
override this with another available `18.x` release (the validation accepts
`18.<minor>`).

## Commands

### Validate locally (no AWS calls)

```bash
cd infra/terraform
terraform fmt -recursive
terraform init -backend=false
terraform validate
```

### Dev

```bash
cd infra/terraform

# Configure the backend first, then:
terraform init
export TF_VAR_db_password='...'
cp envs/dev.tfvars.example envs/dev.tfvars
# Edit envs/dev.tfvars (portal origin, optional alarm email, etc.)

terraform plan -var-file=envs/dev.tfvars -out=dev.tfplan
terraform apply dev.tfplan
```

Dev uses `certificate_arn = ""`, so only the HTTP listener is created.

### Staging

Use a separate state key and copy the dev example, changing
`environment = "staging"`:

```bash
cp envs/dev.tfvars.example envs/staging.tfvars
# edit environment = "staging" and other values
terraform plan -var-file=envs/staging.tfvars -out=staging.tfplan
terraform apply staging.tfplan
```

### Prod

```bash
cp envs/prod.tfvars.example envs/prod.tfvars
# REQUIRED: replace portal_origin and set certificate_arn to a validated ACM
# certificate ARN in var.region. Plan intentionally fails while it is empty.
terraform plan -var-file=envs/prod.tfvars -out=prod.tfplan
terraform apply prod.tfplan
```

The prod example sets `nat_gateway_count = 2`, `multi_az = true`,
`deletion_protection = true`, and `bucket_versioning = false` (ADR-0008).

### Images

The first apply creates the ECR repositories. Push API and worker images to
their repositories after that (or set `api_image`/`worker_image` to images in
another registry). Until an image exists, ECS tasks cannot start. Repositories are
immutable, so push the `bootstrap` tag once and then deploy with a version tag or
digest; production deployments must set an immutable tag or digest.

## Security notes

- **IAM least privilege:** the ECS execution role can only authorize ECR pulls,
  pull from these two repositories, and write to the two log groups. The API task
  role is limited to `f/*` and `staging/*` object operations plus prefix-scoped
  bucket listing. The worker role has only `GetObject`, `PutObject`/`DeleteObject`
  on `orphan/*`, `AbortMultipartUpload`, `ListMultipartUploadParts`, and
  prefix-scoped bucket listing — no other AWS permissions.
- **S3:** Block Public Access is enabled on all four flags, object ownership is
  `BucketOwnerEnforced`, default encryption is SSE-S3, and the bucket policy
  explicitly denies requests where `aws:SecureTransport` is false. The CORS rule
  allows only `PUT` from `var.portal_origin` with `Content-Type`.
- **Database:** RDS is private, encrypted, has no public endpoint, forces TLS,
  and accepts port 5432 only from the API and worker security groups. It has no
  egress rules. The skeleton connects with the master user; provision a
  least-privilege application role (and a separate migration role) before
  production.
- **ALB:** internet-facing security group exposes 80 and 443; egress is scoped to
  the API security group on 8080. With a certificate, port 80 redirects to 443
  using a TLS 1.2/1.3 policy. Without one (dev only), HTTP forwards to the API.
- **Secrets:** `db_password` is sensitive but is still present in Terraform
  state; protect and encrypt the state backend. A Secrets Manager secret holds
  the full Npgsql connection string and is injected as
  `ConnectionStrings__FilesDb` through the ECS `secrets` block; the execution
  role can read only the DB secret plus the ARNs supplied in `app_secrets`.
  Additional secrets (Entra client secret, Graph secret, peppers) must be
  Secrets Manager ARNs (SSM parameters are not permitted by the execution role
  policy). `secret_string` is ignored after creation to support rotation outside
  Terraform. Use RDS-managed credentials and rotation for production.
- **Database backups:** retention is 7 days in dev, 14 in staging, and 35 in prod.
- ECR scan-on-push is enabled and tags are immutable. `:latest` is only a
  bootstrap fallback; deployments must use immutable digests/tags.
- The SNS email subscription requires the recipient to confirm it.

## Intentionally omitted

This skeleton does not implement:

- Route 53 hosted zones/records or DNS delegation
- ACM certificate issuance/validation, or CloudFront
- AWS WAF, Shield Advanced, or rate limiting
- Automatic secret rotation and customer-managed KMS keys
- SigNoz collector/OpenTelemetry sidecar or ADOT configuration
- CI/CD image builds, pushes, deployments, and rollback automation
- S3 access logging, KMS customer-managed keys, Object Lock, or replication
- VPC flow logs, GuardDuty, Security Hub, or AWS Config
- RDS read replicas, enhanced monitoring, or Performance Insights
- Application schema migrations (Flyway is run separately)

Those are deliberate gaps for a skeleton, not production recommendations.
