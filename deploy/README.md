# Deploying Files on the existing host

Files runs on a single, already-provisioned Ubuntu 24.04 LTS host with a public IP and SSH access,
plus an existing S3 bucket that is reachable **only from this host**. There is **no Terraform**:
the VM and the bucket already exist and are managed outside this repository. This directory holds
the production Compose stack, Caddy configuration, the one-time host bootstrap, and the nightly
backup/restore/smoke scripts.

Local development and tests never touch the production host: they run entirely with
`docker-compose.yml` at the repository root (PostgreSQL 18, LocalStack S3, Mailpit, OTel).

## 1. One-time host bootstrap

If the host does not yet have Docker/Compose or the Files directories, run the bootstrap once over
SSH (as a sudo-capable user):

```bash
ssh -i <key>.pem ubuntu@<public-ip> 'sudo bash -s' < deploy/host-bootstrap.sh
```

This installs Docker Engine + Compose plugin, AWS CLI, git/rsync, and creates
`/opt/files` and `/var/lib/files/{staging,postgres,portal}`, plus 2 GiB of swap. It does **not**
touch the firewall (the host's security group/firewall is managed externally) and does **not**
write any secret.

## 2. DNS

At Network Solutions, create an `A` record for `files.sacservices.cloud` pointing to the host's
public IP (TTL 300 while validating). Caddy obtains TLS automatically once ports 80/443 reach the
host.

## 3. First-time setup

```bash
ssh -i <key>.pem ubuntu@<public-ip>

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
`ConnectionStrings__FilesDb` (same password), Entra IDs/audience, `Portal__ClientId` (the SPA app
registration), peppers, the Microsoft 365 SMTP mailbox credentials (`Smtp__*`), and the S3 values: `S3__Region`, `S3__Bucket` (the existing
bucket) and `S3__AccessKeyId` / `S3__SecretAccessKey` (the static key that the host uses — there is
no instance role). Keep `S3__UseSsl=true`. The mailbox must have SMTP AUTH enabled (POP is only for
receiving).

## 4. Start PostgreSQL and migrate

```bash
cd /opt/files/app
docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml up -d postgres
docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml \
  --profile tools run --rm migrator
```

The migrator is never run automatically by the API. Re-run it explicitly after each release.

## 5. Build the SPA and start the stack

```bash
cd web
npm ci
npm run build -- --configuration production
sudo rsync -a --delete dist/web/browser/ /var/lib/files/portal/   # adjust the project name if different
cd ..

docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml \
  up -d --build api worker caddy
```

> `deploy/deploy.sh` (recommended, §7) also writes `/var/lib/files/portal/assets/config.json` from
> the environment (`Entra__TenantId`, `Portal__ClientId`, `Entra__ApiAudience`/`Entra__RequiredScope`,
> `Portal__BaseUrl`), so the SPA gets the real Entra tenant/client IDs at deploy time. The manual
> build above ships the committed placeholder, so populate `web/src/assets/config.json` first if
> you use it.

Caddy obtains the TLS certificate after `files.sacservices.cloud` resolves to the host's public IP
and ports 80/443 are reachable. ClamAV signatures may take a few minutes on first start; wait for
its health before uploading. The ClamAV image is **amd64-only**; the host must be x86_64. Do not
enable scanning on an Apple-Silicon dev host without emulation.

## 6. Backups

Install the cron file and run one backup:

```bash
sudo cp deploy/files-backup.cron /etc/cron.d/files-backup
sudo chmod 644 /etc/cron.d/files-backup
sudo -u ubuntu ./deploy/backup.sh
aws s3 ls "s3://<bucket>/backups/postgres/"   # uses the static keys from /opt/files/.env
```

`backup.sh` and `restore.sh` read `S3__AccessKeyId`/`S3__SecretAccessKey` from `/opt/files/.env`
and export them as `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` for the AWS CLI. The `_latest`
marker is used by the backup-freshness alert. Restore with `deploy/restore.sh` (destructive;
requires `RESTORE_CONFIRM=YES` and a verified dump).

## 7. Deploy updates

Use [`deploy.sh`](deploy.sh) — it is idempotent and fail-fast, and performs the exact sequence:

```bash
cd /opt/files/app
./deploy/deploy.sh <git-tag-or-sha>
```

`deploy.sh` runs, in order: `git fetch` + checkout (refuses a dirty tree) → ensure PostgreSQL →
[`backup.sh`](backup.sh) (records the S3 key) → Flyway `migrate` (explicit `--profile tools`, never
auto) → build the SPA in a `node:22-alpine` container and rsync it to `/var/lib/files/portal` →
`docker compose up -d --build api worker caddy` (images tagged with the release SHA) →
[`smoke.sh`](smoke.sh) → write `/opt/files/REVISION`.

### Rollback

```bash
cd /opt/files/app
./deploy/rollback.sh <previous-tag-or-sha>            # application rollback (preferred)
RESTORE_DUMP=s3://<bucket>/backups/postgres/<file>.dump.gz RESTORE_CONFIRM=YES \
  ./deploy/rollback.sh <previous-tag-or-sha>          # + restore the pre-deploy dump (last resort)
```

Application rollback checks out the previous ref and restarts `api worker caddy` (migrations are
expand/contract, so the schema stays backward-compatible). A database restore is only for a
migration that broke the database and cannot be forward-fixed; it restores the dump recorded in
`/opt/files/REVISION` (`backup=`). See
[`docs/08-operations/deployment-and-rollback.md`](../docs/08-operations/deployment-and-rollback.md).

## 8. Operations

- Logs: `docker compose ... logs -f api worker caddy`; Docker rotation 10 MB × 3.
- Restart one service: `docker compose ... restart api`.
- Validate config: `docker compose --env-file /opt/files/.env -f deploy/docker-compose.prod.yml config`.
- Disk: `df -h /var/lib/files`; staging cleanup is automatic, but investigate growth.
- Optional telemetry: set `SIGNOZ_OTLP_ENDPOINT` and enable `--profile observability`.
- Caddy access logs are disabled intentionally; do not enable them without URI redaction.

## 9. Security reminders

- Only Caddy is public; the API/PostgreSQL are internal.
- S3 is reachable only from this host (enforced by the bucket policy, which is managed outside this
  repo) and is authenticated with a static access key stored only in `/opt/files/.env`. No
  presigned URLs are ever issued.
- `/opt/files/.env` is mode 0600 and never committed.
- Keep SSH restricted (managed externally) and use key auth only.
