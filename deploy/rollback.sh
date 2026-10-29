#!/usr/bin/env bash
# Files — rollback a production deploy.
#
# Usage (on the host, as the deploy user):
#   cd /opt/files/app && ./deploy/rollback.sh [<previous-tag-or-sha>]
#
# Optional database restore (destructive; requires two-person approval per the runbook):
#   RESTORE_DUMP=s3://<bucket>/backups/postgres/<file>.dump.gz RESTORE_CONFIRM=YES ./deploy/rollback.sh <ref>
#
# Behaviour:
#   1. if RESTORE_DUMP is set, restore it into the running PostgreSQL (deploy/restore.sh)
#   2. checkout the previous ref (default: prev_sha recorded in /opt/files/REVISION)
#   3. rebuild/rsync the SPA at that ref and start api/worker/caddy
#   4. run deploy/smoke.sh and re-record /opt/files/REVISION
#
# Application rollback is preferred; a database restore is only for a migration that broke
# the database and cannot be forward-fixed (expand/contract is the norm — see
# docs/08-operations/deployment-and-rollback.md).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/files/app}"
ENV_FILE="${FILES_ENV_FILE:-/opt/files/.env}"
COMPOSE_FILE="$APP_DIR/deploy/docker-compose.prod.yml"
PORTAL_DIR="${PORTAL_DIR:-/var/lib/files/portal}"

DC=(docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE")
get_env() {
  grep -E "^${1}=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//'
}
log() { printf '[rollback] %s\n' "$*"; }
die() { printf '[rollback] ERROR: %s\n' "$*" >&2; exit 1; }

cd "$APP_DIR"

TARGET_REF="${1:-}"
if [ -z "$TARGET_REF" ] && [ -f /opt/files/REVISION ]; then
  TARGET_REF="$(grep -E '^prev_sha=' /opt/files/REVISION | head -n1 | cut -d= -f2- || true)"
fi
[ -n "$TARGET_REF" ] || die "no rollback ref; pass one or restore /opt/files/REVISION"

if [ -n "${RESTORE_DUMP:-}" ]; then
  [ "${RESTORE_CONFIRM:-}" = "YES" ] || die "RESTORE_DUMP set but RESTORE_CONFIRM != YES"
  log "Restoring pre-deploy dump $RESTORE_DUMP ..."
  RESTORE_DUMP="$RESTORE_DUMP" RESTORE_CONFIRM=YES "$APP_DIR/deploy/restore.sh"
fi

log "Checking out $TARGET_REF ..."
git fetch --tags --prune
git checkout "$TARGET_REF"
NEW_SHA="$(git rev-parse HEAD)"
export FILES_VERSION="$NEW_SHA"

log "Building and publishing the SPA at $TARGET_REF ..."
docker build -f "$APP_DIR/web/Dockerfile" --target build -t files-web:build "$APP_DIR/web"
SPA_CID="$(docker create files-web:build)"
trap 'docker rm -f "$SPA_CID" >/dev/null 2>&1 || true' EXIT
docker cp "$SPA_CID:/app/dist/web/browser/." "$PORTAL_DIR/"
docker rm -f "$SPA_CID" >/dev/null
trap - EXIT

log "Starting api/worker/caddy at $TARGET_REF ..."
"${DC[@]}" up -d --build api worker caddy

log "Running smoke tests ..."
FILES_DOMAIN="$(get_env FILES_DOMAIN)" "$APP_DIR/deploy/smoke.sh"

{
  echo "ref=$TARGET_REF"
  echo "sha=$NEW_SHA"
  echo "prev_sha=$(git rev-parse HEAD)"
  echo "backup=rollback"
  echo "deployed_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > /opt/files/REVISION

log "Rollback complete: $TARGET_REF ($NEW_SHA). Record the incident and root cause."
