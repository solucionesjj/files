# ADR-0010 — Initial hosting: one EC2 instance, Docker Compose, Caddy, and one private bucket

- **Status:** Accepted (replaces the ECS/RDS/ALB design for the construction phase)
- **Date:** 2026-10-23
- **Deciders:** Architecture, Operations, Security

## Context

The construction phase has exactly one AWS EC2 machine and one S3 bucket. No ALB, ECS, RDS, ECR,
NAT gateway, or CloudWatch is available. The machine runs Ubuntu 24.04 LTS and has a public IP
exposed to the internet. The product still must be container-first, use PostgreSQL 18, serve an
Angular portal, expose the API, keep S3 private and exclusive to Files, and be reproducible.

## Decision

**Host**

- One EC2 instance, Ubuntu 24.04 LTS x86_64, default `t3.large` (8 GiB, for ClamAV + PostgreSQL + API/worker), 100 GiB gp3 encrypted EBS,
  Elastic IP (stable public address), IMDSv2 required, termination protection enabled.
- Default VPC public subnet; security group allows 80/443 from the internet and 22 only from
  `admin_cidr`. No database port is exposed.
- Bootstrap via Terraform `user_data`: Docker Engine + Compose plugin, AWS CLI, UFW, fail2ban,
  unattended upgrades, directories (`/opt/files`, `/var/lib/files/{staging,postgres}`), 2 GiB swap.

**Runtime (Docker Compose on the host)**

| Container | Exposure | Role |
|---|---|---|
| `caddy` | 80/443 public | TLS termination (automatic HTTPS), reverse proxy `/v1` and `/health` → API, static Angular SPA, security headers, body-size limit. |
| `api` | internal only | ASP.NET Core API, chunk staging, S3 client, auth, audit. |
| `worker` | none | Jobs, outbox, cleanup, deletion, reconciliation, backup trigger. |
| `postgres` | internal only | PostgreSQL 18, data on EBS, healthcheck. |
| `clamav` | internal | clamd on 3310; scans staged files before S3 commit; signature volume. |
| `otel-collector` (optional profile) | internal | OTLP to SigNoz (self-hosted on the same host or external). |

- The API is the **only** S3 client; it uses the EC2 instance role (no static keys).
- Secrets live in `/opt/files/.env` (mode `0600`), loaded by Compose and provisioned by the
  deployment pipeline. No AWS Secrets Manager in this phase.
- PostgreSQL backups: nightly `pg_dump` (`-Fc`, gzip) from the host to
  `s3://<bucket>/backups/postgres/`, retained 35 days by lifecycle. No PITR/WAL archiving.
- Production name: **`files.sacservices.cloud`** (DNS at Network Solutions, A record to the
  Elastic IP). Caddy obtains/renews the certificate automatically. A bare IP is only for temporary
  tests with a self-signed cert (OQ-15/OQ-26).

**Provisioning:** Terraform covers the instance, Elastic IP, security group, IAM role/profile,
the S3 bucket, and the restrictive bucket policy. Application deployment is Git + SSH + Docker
Compose (see `deploy/`).

## Consequences

- Very low cost and a small operational surface, at the price of a **single point of failure**:
  no HA, no automatic failover, and planned maintenance windows.
- Availability target is 99.5% best effort; RPO ≤24 h and RTO ≤4 h (D-31). These are honest limits
  for one host and logical backups.
- 5 GiB uploads and ClamAV scans are bounded by the instance's network, CPU, and EBS; disk and
  memory monitoring are critical. ClamAV needs ~1.5–2 GiB; `t3.medium` (4 GiB) is too tight.
- The API/worker, PostgreSQL, and ClamAV compete for 8 GiB of RAM; resource limits and swap are
  mandatory.
- Patching the host requires a short maintenance window (`apt` + Docker restart). Containers
  restart automatically.
- Scaling path (when construction ends): move PostgreSQL to RDS, run 2+ API tasks on ECS/ALB (or
  keep Compose on a larger EC2), add CloudWatch/SigNoz, PITR, WAF, and a second AZ. The API/S3
  boundary stays the same.
- A public IP without a domain cannot get a trusted certificate; the domain/DNS decision gates
  public launch.

## Alternatives considered

- **ECS Fargate + ALB + RDS + ECR** (previous design): rejected for the current phase because
  those services are not available; kept as the scale-up path.
- **Lightsail / another VPS:** could work, but the user has an EC2 instance and S3; no benefit now.
- **Bare-metal PostgreSQL on the host:** viable, but containers keep deployment reproducible and
  match local development.
- **Nginx instead of Caddy:** Nginx requires manual certbot wiring; Caddy automates TLS and static
  serving with a simpler config.
