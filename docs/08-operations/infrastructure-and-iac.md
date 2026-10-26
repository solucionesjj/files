# Infrastructure, Environments, and Host Setup

**Decisions:** ADR-0010, ADR-0003, D-30…D-33 · **Host setup:** [`deploy/host-bootstrap.sh`](../../deploy/host-bootstrap.sh) ·
**Deploy:** [`deploy/README.md`](../../deploy/README.md) · **Costs:** [cost-model.md](cost-model.md)

## 1. Topology

One **pre-existing** Ubuntu 24.04 LTS host (≥2 vCPU / 8 GiB, public IP, SSH) and one
**pre-existing** S3 bucket, reachable only from that host. There is **no Terraform**: the VM and
the bucket are provisioned and managed outside this repository. No ALB, ECS, RDS, ECR, NAT gateway,
or CloudWatch is used.

| Resource | Value |
|---|---|
| Host | Ubuntu 24.04 LTS x86_64 (≥2 vCPU, ≥8 GiB), public IP, SSH; firewall/security group managed externally |
| Address | Static public IP (no instance is created here) |
| S3 | One existing private bucket, SSE-S3, BPA, versioning off; policy/lifecycle managed externally |
| Auth to S3 | Static access key in `/opt/files/.env` (no instance role) |
| Host software | Docker Engine + Compose plugin, AWS CLI (installed once by `deploy/host-bootstrap.sh`) |
| DNS/TLS | A record → host public IP; Caddy automatic HTTPS (domain required) |

Environments: **production only**. Development and testing run entirely on the developer machine
with `docker-compose.yml` at the repository root (PostgreSQL 18, LocalStack S3, Mailpit, OTel). Do
not deploy to the production host from local development.

## 2. Host bootstrap

Run **once**, over SSH, before the first deployment:

```bash
ssh -i <key>.pem ubuntu@<public-ip> 'sudo bash -s' < deploy/host-bootstrap.sh
```

The script installs Docker Engine + Compose plugin, AWS CLI, git/rsync, and creates
`/opt/files` and `/var/lib/files/{staging,postgres,portal}` plus 2 GiB of swap. It does **not**
touch the firewall (managed externally) and does **not** write secrets. It is safe to re-run.

## 3. First deployment steps

1. Point DNS `files.sacservices.cloud` (Network Solutions) at the host's public IP.
2. Run `deploy/host-bootstrap.sh` once (if not already done).
3. `git clone` the repository to `/opt/files/app`.
4. Create `/opt/files/.env` from `deploy/env.prod.example` (mode 0600) with the DB password, Entra
   IDs/secrets, peppers, M365 SMTP mailbox, and the S3 bucket name + static access key.
5. Start PostgreSQL, run migrations, build/start API/worker/Caddy, copy the SPA build.
6. Install the nightly backup cron and verify one dump in S3.

Full commands are in `deploy/README.md`.

## 4. Networking and exposure

- Internet → host firewall 80/443 → Caddy. SSH is restricted by the externally managed firewall.
- API `:8080` is not published; Caddy reaches it over the Docker network.
- PostgreSQL has no host port; it is reachable only by API/worker containers.
- Host egress 443 to S3, Entra, ClamAV signatures, Docker Hub/GitHub, and SigNoz; **587 to
  `smtp.office365.com`** for email. No inbound database access.

## 5. S3 access model (exclusive)

- The API/worker authenticate to S3 with a static access key stored only in `/opt/files/.env`
  (`S3__AccessKeyId` / `S3__SecretAccessKey`). The host has no instance role.
- The existing bucket policy already restricts object access to this host and is managed outside
  this repository; do not modify it. No presigned URLs and no S3 CORS; the browser never talks to
  S3. The API CORS allowlist is used only for local development.
- Object keys are `f/{fileId}`, `staging/`, `orphan/`, and `backups/`; the API/worker is the only
  reader/writer.
- Lifecycle (abort incomplete multipart after 7 days; expire `staging/` after 1 day, `orphan/`
  after 7 days, `backups/postgres/` after 35 days; versioning disabled per ADR-0008) is expected to
  already exist on the bucket. Verify it once during the first deployment; if a rule is missing,
  raise it with the bucket owner rather than editing the bucket directly.

## 6. Secrets and configuration

- `/opt/files/.env` (0600, owned by the deploy user) contains:
  `POSTGRES_PASSWORD`, `ConnectionStrings__FilesDb`, `Entra__*`, `Graph__*`,
  `Auth__LinkKeyPepper`, `Notifications__*`, `Storage__StagingPath`, `S3__Bucket`, `S3__Region`,
  `S3__AccessKeyId`, `S3__SecretAccessKey`, `OTEL_*`, feature flags.
- The API/worker and the backup/restore scripts read the S3 static key from this file; it is never
  committed, logged, or baked into an image.
- Local development uses `.env` with LocalStack credentials; those are non-secret.
- Rotation: DB password (update `.env` + `ALTER ROLE`, restart API), peppers (versioned columns),
  SMTP/Entra secrets (Microsoft 365/Entra + `.env` + restart), S3 key (rotate the IAM key, update
  `.env`, restart API/worker), SSH keys. See [runbooks.md](runbooks.md).

## 7. TLS and DNS

- A public IPv4 alone cannot obtain a publicly trusted certificate; a DNS name is required (OQ-15).
- The domain may be managed anywhere (Route 53 is not required). Create an `A` record to the host's
  public IP.
- Caddy obtains/renews the certificate automatically (HTTP-01/TLS-ALPN) once ports 80/443 reach
  the host. It also redirects HTTP to HTTPS and sets HSTS.
- For a temporary test without a domain, use `sslip.io`/`nip.io` pointing at the host IP, or a
  self-signed cert restricted to test IPs. Do not expose self-signed TLS as production.
- Caddy access logs are disabled (token-bearing URIs); see threat model I-11.

## 8. Backups and disaster recovery

| Asset | Backup | Retention | Restore |
|---|---|---|---|
| PostgreSQL | Nightly `pg_dump -Fc` gzip to `s3://<bucket>/backups/postgres/` via host cron | 35 days (lifecycle) | Download, `pg_restore` into a fresh container/instance |
| S3 objects | Provider durability; versioning disabled | n/a | Objects remain unless deleted |
| Config | `.env` backed up in the team secret store | n/a | Re-provision |
| Images | Rebuild from Git tag | Git history | Rebuild/restart |

- Initial **RPO ≤24 h** and **RTO ≤4 h** (D-31). PITR/WAL archiving is not available until the DB
  moves to a managed service. S3 has versioning/replication disabled, so object loss is a
  documented residual risk; enable one of them before the data becomes critical.
- The backup job records success/failure in the journal and a `backups/postgres/_latest` marker;
  a worker job verifies the marker exists and is <26 h old, otherwise alerts.
- Quarterly restore drill into a fresh host; record the result in
  [runbooks.md](runbooks.md#drill-log).

## 9. Observability infrastructure

- ClamAV (`clamd` 3310) scans staged files before S3; signatures on a 2 GiB volume; one scan at a
  time. The published image is amd64-only (the host is x86_64); local Apple Silicon development
  keeps scanning off.
- OTel Collector container (optional profile) exports OTLP to SigNoz (external is preferred over
  self-hosting on the same host alongside ClamAV).
- Host metrics: `docker stats`, `df`, `free`, `systemd` logs; `node_exporter` if SigNoz is added.
- Logs: Docker `json-file` with rotation (10 MB × 3) or journald; no access logs for token-bearing
  paths.
- Uptime: an external HTTPS check against `/health/ready` (and the SPA) is recommended.
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

`docker-compose.yml` provides PostgreSQL 18, LocalStack S3, Mailpit, and an OTel Collector. Flyway
applies migrations via the `tools` profile. The API/worker run on the host with `dotnet run`. See
the root `README.md`.

This stack is for development only: LocalStack credentials are non-secret, Mailpit accepts any
mail, and the DB has a known password. The SPA runs via `ng serve` at `localhost:4200`; no S3 CORS
is required.

## 12. Scale-up path

1. Add a second host and a load balancer (or move to ECS/ALB).
2. Move PostgreSQL to RDS for PITR/Multi-AZ.
3. Move staging to shared/durable storage if multiple API instances write uploads (or upload
   directly from each API instance to S3 multipart with session affinity).
4. Add CloudWatch/WAF/Route 53 or keep Caddy/DNS, plus centralized logs/SigNoz.
