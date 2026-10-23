# ADR-0010 — AWS hosting, container-first delivery, environment secrets

- **Status:** Provisional (OQ-02, OQ-15)
- **Date:** 2026-10-23
- **Deciders:** Architecture, Operations

## Context

S3 is mandated. The source requires container-first delivery, three environments, IaC, and
"secrets in environment files for the moment". The team is small; operational overhead must stay
low while keeping private networking and least-privilege IAM.

## Decision

**Platform:** AWS, one account per environment (`files-dev`, `files-test`, `files-prod`), region
`us-east-1` (OQ-02), Terraform-managed.

**Compute:** ECS Fargate services behind an ALB:

- `api` service: 2 tasks min in prod (0.5 vCPU/1 GiB each), autoscale 2–8 on CPU 60%/req count.
- `worker` service: 1 task (0.5 vCPU/1 GiB), no public ingress.
- Images in ECR, immutable tags `<git-sha>`; `latest` is never deployed.

**Data:** RDS PostgreSQL 18, `db.t4g.medium` Multi-AZ in prod (single-AZ dev/test), gp3 100 GiB
autoscaling, encryption at rest, automated backups 35 days, PITR, deletion protection in prod.

**Network:** VPC with 2 public + 2 private subnets, one NAT gateway in dev/test, two in prod
(or one + VPC endpoints if cost-driven). S3 and ECR accessed via VPC gateway/interface endpoints
where practical to reduce NAT cost.

**Storage:** private S3 bucket per environment; Block Public Access; SSE-S3 by default (SSE-KMS
optional with a customer-managed key); versioning disabled (ADR-0008); lifecycle rules for
`staging/` (1 day) and incomplete multipart uploads (7 days).

**Secrets:** non-secret configuration is injected as environment variables (matching the source
constraint). The database connection string is stored in AWS Secrets Manager and injected through
the ECS `secrets` block; other sensitive values (Entra/Graph client secrets, peppers) use the same
mechanism via an `app_secrets` map. Local development uses `.env`. Terraform state contains the DB
password, so the state backend must be encrypted and access-controlled; automatic rotation is a
documented follow-up. Secrets are never committed; `.env` is gitignored; `.env.example` contains
placeholders.

**Delivery:** GitHub Actions builds/tests, pushes images, runs migrations as a one-off ECS task,
then deploys with the ECS circuit breaker (rollback on failure). Terraform applies per environment
from a protected branch.

## Consequences

- Fargate removes host patching; the trade-off is per-task cost and cold starts (mitigated by
  min capacity ≥2 in prod).
- Environment variables are visible to anyone with task-definition read access; IAM and account
  boundaries are the control, and Secrets Manager is the planned upgrade.
- Single-region; disaster recovery is restore-based (ADR-0008, DR doc).
- Public SPA hosting: S3+CloudFront preferred; ALB static hosting acceptable for v1. Decide at
  deployment time without changing the app.

## Alternatives considered

- EKS/Kubernetes: rejected (operational overhead for two services).
- App Runner: viable but weaker VPC/ALB integration and less control over task networking.
- EC2 + Docker Compose: rejected (patching, no rolling deploys).
- Azure Container Apps: rejected because S3 is mandated and cross-cloud egress/IAM is worse.
