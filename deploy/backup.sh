#!/usr/bin/env bash
# Nightly logical backup: pg_dump -> gzip -> S3 backups/postgres/.
# Runs on the host as the deploy user; authenticates to S3 with the static
# access keys read from /opt/files/.env (there is no instance role).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/files/app}"
ENV_FILE="${FILES_ENV_FILE:-/opt/files/.env}"
COMPOSE_FILE="${COMPOSE_FILE:-$APP_DIR/deploy/docker-compose.prod.yml}"
TMP_DIR="${TMP_DIR:-/var/tmp}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

get_env() {
  # Read a single KEY=VALUE without sourcing the file (connection strings contain ';').
  local key="$1"
  grep -E "^${key}=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//'
}

POSTGRES_DB="$(get_env POSTGRES_DB)"
POSTGRES_USER="$(get_env POSTGRES_USER)"
S3_BUCKET="$(get_env S3__Bucket)"
S3_REGION="$(get_env S3__Region)"
export AWS_ACCESS_KEY_ID="$(get_env S3__AccessKeyId)"
export AWS_SECRET_ACCESS_KEY="$(get_env S3__SecretAccessKey)"
export AWS_DEFAULT_REGION="$S3_REGION"

: "${POSTGRES_DB:?missing POSTGRES_DB in $ENV_FILE}"
: "${POSTGRES_USER:?missing POSTGRES_USER in $ENV_FILE}"
: "${S3_BUCKET:?missing S3__Bucket in $ENV_FILE}"
: "${AWS_ACCESS_KEY_ID:?missing S3__AccessKeyId in $ENV_FILE}"
: "${AWS_SECRET_ACCESS_KEY:?missing S3__SecretAccessKey in $ENV_FILE}"

DUMP="$TMP_DIR/files-${STAMP}.dump.gz"
trap 'rm -f "$DUMP"' EXIT

echo "Dumping $POSTGRES_DB ..."
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" exec -T postgres \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner \
  | gzip -9 > "$DUMP"

if [ ! -s "$DUMP" ]; then
  echo "Backup file is empty; aborting" >&2
  exit 1
fi

KEY="backups/postgres/files-${STAMP}.dump.gz"
echo "Uploading s3://$S3_BUCKET/$KEY ..."
aws s3 cp "$DUMP" "s3://$S3_BUCKET/$KEY" --region "$S3_REGION" --sse AES256 --only-show-errors
printf '%s' "$STAMP" | aws s3 cp - "s3://$S3_BUCKET/backups/postgres/_latest" \
  --region "$S3_REGION" --sse AES256 --only-show-errors

echo "Backup complete: s3://$S3_BUCKET/$KEY"
