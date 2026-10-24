# Deploying Files on the single EC2 host

This directory contains the production Compose stack, Caddy configuration, the nightly backup,
restore and smoke scripts. It assumes Terraform has created the instance, Elastic IP, and bucket,
and that DNS points to the Elastic IP.

## 1. DNS

At Network Solutions, create an `A` record for `files.sacservices.cloud` pointing to the Elastic IP
(TTL 300 while validating). Wait for propagation; Caddy will obtain TLS automatically once ports
80/443 reach the instance.

## 2. First-time setup

```bash
ssh -i <key>.pem ubuntu@<elastic-ip>

sudo mkdir -p /opt/files
sudo chown ubuntu:ubuntu /opt/files
git clone <repo> /opt/files/app
cd /opt/files/app

# copy and edit the environment
cp deploy/env.prod.example /opt/files/.env
chmod 600 /opt/files/.env
$EDITOR /opt/files/.env
```

Fill at least: `FILES_DOMAIN=files.sacservices.cloud`, `POSTGRES_PASSWORD`,
`ConnectionStrings__FilesDb` (same password), Entra IDs/audience, peppers, the Microsoft 365 SMTP
mailbox credentials (`Smtp__*`), and the S3 bucket name. Do not add S3 access keys; the instance
role is used. Keep `S3__Region=us-east-1`. The mailbox must have SMTP AUTH enabled (POP is only for
receiving). For private-link ACLs, grant the Graph app `User.ReadBasic.All`,
`Group.ReadBasic.All`, and `GroupMember.Read.All` (admin consent) and set `Graph__*`.

## 3. Start PostgreSQL and migrate

```bash
cd /opt/files/app
docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml up -d postgres
docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml \
  --profile tools run --rm migrator
```

The migrator is never run automatically by the API. Re-run it explicitly after each release.

## 4. Build the SPA and start the stack

```bash
cd web
npm ci
npm run build -- --configuration production
sudo rsync -a --delete dist/web/browser/ /var/lib/files/portal/   # adjust the project name if different
cd ..

docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml \
  up -d --build api worker caddy
```

The API/worker Dockerfiles (`src/Files.Api/Dockerfile`, `src/Files.Worker/Dockerfile`) are created
in S1. The API must include `curl` for its healthcheck. Caddy obtains the TLS certificate after
`files.sacservices.cloud` resolves to the Elastic IP and ports 80/443 reach the host. ClamAV
signatures may take a few minutes on first start; wait for its health before uploading. The
ClamAV image is **amd64-only**; the production EC2 is x86_64. Do not enable scanning on an
Apple-Silicon dev host without emulation.

## 5. Backups

Install the cron file and run one backup:

```bash
sudo cp deploy/files-backup.cron /etc/cron.d/files-backup
sudo chmod 644 /etc/cron.d/files-backup
sudo -u ubuntu ./deploy/backup.sh
aws s3 ls "s3://<bucket>/backups/postgres/"   # requires the instance role / awscli
```

The `_latest` marker is used by the backup-freshness alert. Restore with `deploy/restore.sh`
(destructive; requires `RESTORE_CONFIRM=YES` and a verified dump).

## 6. Deploy updates

```bash
cd /opt/files/app
git fetch --tags
git checkout <tag>
./deploy/backup.sh
docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml \
  --profile tools run --rm migrator
docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml \
  up -d --build api worker caddy
./deploy/smoke.sh
```

Record the deployed tag in `/opt/files/REVISION`. Rollback by checking out the previous tag and
re-running the same compose command (migrations are expand/contract; restore the pre-deploy dump
only as a last resort).

## 7. Operations

- Logs: `docker compose ... logs -f api worker caddy`; Docker rotation 10 MB × 3.
- Restart one service: `docker compose ... restart api`.
- Validate config: `docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml config`.
- Disk: `df -h /var/lib/files`; staging cleanup is automatic, but investigate growth.
- Optional telemetry: set `SIGNOZ_OTLP_ENDPOINT` and enable `--profile observability`.
- Caddy access logs are disabled intentionally; do not enable them without URI redaction.

## 8. Security reminders

- Only Caddy is public; the API/PostgreSQL are internal.
- S3 is reachable only by the instance role; no presigned URLs, no static keys.
- `/opt/files/.env` is mode 0600 and never committed.
- Keep SSH restricted to `admin_cidr`; use key auth only.
