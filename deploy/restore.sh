#!/usr/bin/env bash
# Restore a pg_dump into the running PostgreSQL container. DESTRUCTIVE.
# Usage: RESTORE_DUMP=s3://bucket/backups/postgres/file.dump.gz RESTORE_CONFIRM=YES ./restore.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/files/app}"
ENV_FILE="${FILES_ENV_FILE:-/opt/files/.env}"
COMPOSE_FILE="${COMPOSE_FILE:-$APP_DIR/deploy/docker-compose.prod.yml}"
DUMP="${RESTORE_DUMP:?set RESTORE_DUMP to an s3:// URI or local .dump.gz path}"

if [ "${RESTORE_CONFIRM:-}" != "YES" ]; then
  echo "Refusing to restore without RESTORE_CONFIRM=YES" >&2
  exit 1
fi

get_env() {
  local key="$1"
  grep -E "^${key}=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//'
}
POSTGRES_DB="$(get_env POSTGRES_DB)"
POSTGRES_USER="$(get_env POSTGRES_USER)"
S3_REGION="$(get_env S3__Region)"
export AWS_ACCESS_KEY_ID="$(get_env S3__AccessKeyId)"
export AWS_SECRET_ACCESS_KEY="$(get_env S3__SecretAccessKey)"
export AWS_DEFAULT_REGION="$S3_REGION"

LOCAL_DUMP="/var/tmp/restore-$(date -u +%Y%m%dT%H%M%SZ).dump.gz"
trap 'rm -f "$LOCAL_DUMP"' EXIT

case "$DUMP" in
  s3://*) aws s3 cp "$DUMP" "$LOCAL_DUMP" --only-show-errors ;;
  *) cp "$DUMP" "$LOCAL_DUMP" ;;
esac

echo "Stopping API and worker ..."
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" stop api worker

echo "Restoring into $POSTGRES_DB (this replaces objects) ..."
gunzip -c "$LOCAL_DUMP" | docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" exec -T postgres \
  pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists --no-owner

echo "Starting API and worker ..."
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" start api worker
echo "Restore finished. Run deploy/smoke.sh and verify data."
