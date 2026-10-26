# ADR-0010 — Initial hosting: one existing host, Docker Compose, Caddy, and one private bucket

- **Status:** Accepted (amended 2026-10-25: infrastructure is pre-existing, S3 uses a static key)
- **Date:** 2026-10-23
- **Deciders:** Architecture, Operations, Security

> **Amendment (2026-10-25).** The EC2 instance and S3 bucket already exist and are managed outside
> this repository; Terraform no longer provisions anything and was removed. S3 access uses a static
> access key stored in `/opt/files/.env` (the host has no instance role), and the existing bucket
> policy already restricts object access to this host. The text below reflects this; the superseded
> "instance role / Terraform" wording is kept only where it describes history.

## Context

The construction phase has exactly one AWS EC2 machine and one S3 bucket. No ALB, ECS, RDS, ECR,
NAT gateway, or CloudWatch is available. The machine runs Ubuntu 24.04 LTS and has a public IP
exposed to the internet. The product still must be container-first, use PostgreSQL 18, serve an
Angular portal, expose the API, keep S3 private and exclusive to Files, and be reproducible.

## Decision

**Host**

- One pre-existing Ubuntu 24.04 LTS x86_64 host (≥2 vCPU / 8 GiB for ClamAV + PostgreSQL + API/worker) with a public IP and SSH access. No instance is provisioned by this repository.
- The host's firewall/security group is managed externally and already exposes only 80/443 (and SSH to the administrator). No database port is exposed.
- One-time bootstrap via `deploy/host-bootstrap.sh`: Docker Engine + Compose plugin, AWS CLI,
  directories (`/opt/files`, `/var/lib/files/{staging,postgres,portal}`), 2 GiB swap.

**Runtime (Docker Compose on the host)**

| Container | Exposure | Role |
|---|---|---|
| `caddy` | 80/443 public | TLS termination (automatic HTTPS), reverse proxy `/v1` and `/health` → API, static Angular SPA, security headers, body-size limit. |
| `api` | internal only | ASP.NET Core API, chunk staging, S3 client, auth, audit. |
| `worker` | none | Jobs, outbox, cleanup, deletion, reconciliation, backup trigger. |
| `postgres` | internal only | PostgreSQL 18, data on EBS, healthcheck. |
| `clamav` | internal | clamd on 3310; scans staged files before S3 commit; signature volume. |
| `otel-collector` (optional profile) | internal | OTLP to SigNoz (self-hosted on the same host or external). |

- The API is the **only** S3 client; it authenticates with a static access key stored in
  `/opt/files/.env` (the host has no instance role). The existing bucket's policy already restricts
  object access to this host.
- Secrets live in `/opt/files/.env` (mode `0600`), loaded by Compose and provisioned by the
  deployment pipeline. No AWS Secrets Manager in this phase.
- PostgreSQL backups: nightly `pg_dump` (`-Fc`, gzip) from the host to
  `s3://<bucket>/backups/postgres/`, retained 35 days by lifecycle. No PITR/WAL archiving.
- Production name: **`files.sacservices.cloud`** (DNS at Network Solutions, A record to the host's
  public IP). Caddy obtains/renews the certificate automatically. A bare IP is only for temporary
  tests with a self-signed cert (OQ-15/OQ-26).

**Provisioning:** the host and the S3 bucket already exist and are managed outside this repository
(no Terraform). Application deployment is Git + SSH + Docker Compose (see `deploy/`), with a
one-time host bootstrap via `deploy/host-bootstrap.sh`.

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
